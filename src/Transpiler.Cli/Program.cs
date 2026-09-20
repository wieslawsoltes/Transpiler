using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Transpiler.Backends;
using Transpiler.Core;
using Transpiler.Frontend.Roslyn;

namespace Transpiler.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static int Main(string[] args)
    {
        Options? options = null;
        CompilationInputSession? inputSession = null;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { Console.WriteLine(Help); return 0; }
            options = Parse(args);
            if (options.Target is "cpp" or "native-std")
            {
                if (options.DispatchExplicit && options.Dispatch != "ssa") throw new ArgumentException("native-std supports --dispatch ssa only.");
                options.Dispatch = "ssa";
            }
            var inputs = inputSession = new CompilationInputSession(new(options.MaxInputBytes, options.MaxTotalInputBytes, options.MaxInputFiles), cancellation.Token);
            if (options.Command == "capabilities")
            {
                var text = JsonSerializer.Serialize(new
                {
                    schema = 1, profile = "portable-mvp", targets = new[] { "javascript", "python" }, dispatchModes = new[] { "instruction", "block", "ssa" },
                    nativeStd = new { profile = CppNativeStdEmitter.Profile, targets = new[] { "native-std", "cpp" }, types = CppNativeStdEmitter.SupportedTypes, opcodes = CppNativeStdEmitter.SupportedOpcodes, libraryOnly = true },
                    opcodes = CompilerAnalysis.SupportedOpcodes, intrinsics = IntrinsicCatalog.All,
                    limitations = new[] { "explicit multi-assembly linking; one version per name", "bounded closed generics; no dynamic generic loading", "limited type identity only; no member reflection or native interop", "two-pass managed filters; no native exception/stack trace parity", "not a security verifier" }
                }, Json);
                if (options.Output is null) Console.WriteLine(text); else Write(options.Output, text);
                return 0;
            }
            if (options.Inputs.Count == 0) throw new ArgumentException("At least one input is required.");
            ValidateInputOptions(options);
            CompilationInputLock? expectedLock = null;
            if (options.InputLock is not null)
            {
                var file = inputs.Read(options.InputLock);
                if (file.Content.Length > 4 * 1024 * 1024) throw new CompilationException(new Diagnostic("TR3310", "Input lock exceeds 4 MiB."));
                expectedLock = CompilationInputLock.Parse(Encoding.UTF8.GetString(file.Content.AsSpan()));
            }
            var source = options.Inputs.All(p => Path.GetExtension(p).Equals(".cs", StringComparison.OrdinalIgnoreCase));
            if (!source && options.Inputs.Count != 1) throw new ArgumentException("Supply C# source files or exactly one managed assembly.");
            if (options.Inputs.Any(p => !File.Exists(p))) throw new FileNotFoundException("An input file does not exist.");
            ManagedCompilation? managed = null;
            byte[] image;
            if (source)
            {
                var files = options.Inputs.Select(p => new SourceFile(Path.GetFileName(p), ReadText(inputs.Read(p)))).ToArray();
                if (files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != files.Length)
                    throw new ArgumentException("Source file names must be unique within one compilation.");
                managed = RoslynFrontend.Compile(files, Path.GetFileNameWithoutExtension(options.Inputs[0]), options.Library, !options.Debug, options.References, options.ReferencePack, inputs);
                image = managed.Pe;
            }
            else image = inputs.Read(options.Inputs[0]).Content.ToArray();
            if (options.Command == "emit-pe")
            {
                var output = RequireOutput(options);
                Write(output, image);
                if (managed is not null) Write(Path.ChangeExtension(output, ".pdb"), managed.Pdb);
                if (!options.Library)
                    Write(Path.ChangeExtension(output, ".runtimeconfig.json"), JsonSerializer.Serialize(new
                    {
                        runtimeOptions = new { tfm = "net10.0", rollForward = "LatestMinor", framework = new { name = "Microsoft.NETCore.App", version = "10.0.0" } }
                    }, Json));
                Console.WriteLine(JsonSerializer.Serialize(new { output, bytes = image.Length, compiler = managed?.CompilerVersion, host = Environment.Version.ToString() }));
                return 0;
            }
            var assembly = AssemblyImporter.Read(image, cancellationToken: cancellation.Token);
            if (options.Command == "inspect")
            {
                var text = JsonSerializer.Serialize(assembly, Json);
                if (options.Output is null) Console.WriteLine(text); else Write(options.Output, text);
                return 0;
            }
            assembly = PortableCompilation.Link(assembly, options.References,
                new LinkOptions(options.Bcl, options.ReferencePack, options.CoreLibrary) { ReferenceDirectories = options.ReferenceDirectories }, inputs);
            var targetName = options.Target switch { "js" or "javascript" => "javascript", "py" or "python" => "python", "cpp" or "native-std" => "native-std", _ => options.Target };
            var inputLock = options.InputLock is not null || options.WriteInputLock is not null ? CompilationInputLock.Create(
                targetName ?? throw new ArgumentException("An input lock requires --target."), options.Dispatch,
                options.Bcl ? LibrarySubstitution.Policy : "none", assembly.Inputs,
                managed?.ReferencePack?.Inputs ?? (options.Bcl ? ReferencePackResolver.Resolve(options.ReferencePack, inputs).Inputs : []),
                CompilerInputs(inputs)) : null;
            inputLock?.Serialize(); // Validate before any output write, even when recording a new lock.
            expectedLock?.Verify(inputLock!);
            RejectSnapshotOverwrite(options, inputs);
            cancellation.Token.ThrowIfCancellationRequested();
            var analysis = CompilerAnalysis.Analyze(assembly, cancellation.Token);
            if (options.Dispatch == "ssa") analysis = StackSsa.Prepare(analysis, cancellation.Token);
            if (options.Command == "analyze")
            {
                var text = JsonSerializer.Serialize(analysis, Json);
                if (options.Output is null) Console.WriteLine(text); else Write(options.Output, text);
                return 0;
            }
            var target = options.Target switch
            {
                "js" or "javascript" => SourceTarget.JavaScript,
                "py" or "python" => SourceTarget.Python,
                "cpp" or "native-std" => SourceTarget.NativeStd,
                _ => throw new ArgumentException("--target must be javascript (js), python (py) or native-std (cpp).")
            };
            var dispatch = options.Dispatch switch
            {
                "instruction" => DispatchMode.Instruction,
                "block" => DispatchMode.BasicBlock,
                "ssa" => DispatchMode.StackSsa,
                _ => throw new ArgumentException("--dispatch must be instruction, block or ssa.")
            };
            var generated = SourceEmitter.Emit(analysis, target, new SourceEmissionOptions(dispatch), cancellation.Token);
            var destination = RequireOutput(options);
            cancellation.Token.ThrowIfCancellationRequested();
            if (options.IrOutput is not null) Write(options.IrOutput, JsonSerializer.Serialize(analysis, Json));
            Write(destination, generated.Text);
            if (options.Manifest is not null) Write(options.Manifest, JsonSerializer.Serialize(new
            {
                schema = 2, target = target.ToString(), profile = generated.Profile, nativeExports = generated.NativeExports, dispatch = options.Dispatch, ssa = new { generated.SsaMethodCount, generated.SsaEliminatedInstructions, generated.SsaFallbacks }, bcl = options.Bcl ? LibrarySubstitution.Policy : "none",
                forwardings = assembly.ForwardingBindings, assemblies = assembly.Inputs, referencePack = managed?.ReferencePack?.Inputs,
                dependencyBindings = assembly.DependencyBindings,
                inputLock = inputLock is null ? null : new { policy = CompilationInputLock.CurrentPolicy, verified = expectedLock is not null,
                    sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(inputLock.Serialize()))).ToLowerInvariant() },
                inputIo = new { inputs.FileCount, inputs.BytesRead },
                transpiled = analysis.Methods.Where(m => m.Method.Instructions.Length != 0)
                    .Select(m => new { assembly = m.Method.Reference.Assembly, method = m.Method.Key, instructions = m.Method.Instructions.Length }),
                intrinsics = analysis.Methods.SelectMany(m => m.Method.Instructions).Select(i => i.Operand).OfType<MethodReference>()
                    .Where(m => analysis.Assembly.Resolve(m) is null).Select(m => new { method = m.Key, binding = IntrinsicCatalog.Find(m, analysis.Assembly) }).Distinct()
            }, Json));
            if (options.WriteInputLock is not null) Write(options.WriteInputLock, inputLock!.Serialize());
            if (options.Diagnostics is not null) Write(options.Diagnostics, "[]\n");
            Console.WriteLine(JsonSerializer.Serialize(new { output = destination, target = target.ToString(), generated.MethodCount, generated.InstructionCount, generated.DispatchCaseCount, generated.SsaMethodCount, generated.SsaEliminatedInstructions, generated.SsaFallbacks, dispatch = options.Dispatch, bytes = Encoding.UTF8.GetByteCount(generated.Text), profile = generated.Profile, nativeExports = generated.NativeExports }));
            return 0;
        }
        catch (OperationCanceledException)
        {
            var diagnostic = new Diagnostic("TR0003", "Compilation canceled.");
            Console.Error.WriteLine(diagnostic);
            if (options?.Diagnostics is { } path && inputSession?.ContainsPath(path) != true) TryWriteDiagnostics(path, [diagnostic]);
            return 130;
        }
        catch (CompilationException error)
        {
            foreach (var diagnostic in error.Diagnostics) Console.Error.WriteLine(diagnostic);
            if (options?.Diagnostics is { } path && inputSession?.ContainsPath(path) != true) TryWriteDiagnostics(path, error.Diagnostics);
            return 1;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("TR0001: " + error.Message);
            if (options?.Diagnostics is { } path && inputSession?.ContainsPath(path) != true) TryWriteDiagnostics(path, [new Diagnostic("TR0001", error.Message)]);
            return 2;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static string ReadText(CompilationFile file)
    {
        using var reader = new StreamReader(new MemoryStream(file.Content.ToArray(), writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static AssemblyInput[] CompilerInputs(CompilationInputSession inputs) =>
        RoslynFrontend.CompilerInputs(inputs).Concat(new[] { typeof(Program).Assembly, typeof(SourceEmitter).Assembly }
            .Select(a => inputs.Read(a.Location).Fingerprint(a.GetName().FullName!))).OrderBy(a => a.Identity, StringComparer.Ordinal).ToArray();

    private static void ValidateInputOptions(Options options)
    {
        var locked = options.InputLock is not null || options.WriteInputLock is not null;
        if (options.InputLock is not null && options.WriteInputLock is not null)
            throw new ArgumentException("--input-lock and --write-input-lock are mutually exclusive.");
        if (locked && options.Command != "compile") throw new ArgumentException("Input locks apply to compile; emit-pe first for independent assembly replay.");
        if (options.ReferenceDirectories.Count != 0 && options.Command is not ("compile" or "analyze"))
            throw new ArgumentException("--reference-dir applies to compile/analyze only.");
        var reads = options.Inputs.Concat(options.References).Concat(new[] { options.InputLock, options.CoreLibrary }.OfType<string>())
            .Select(Path.GetFullPath).ToHashSet(CompilationInputSession.PathComparer);
        var writes = new[] { options.Output, options.IrOutput, options.Manifest, options.Diagnostics, options.WriteInputLock }.OfType<string>()
            .Select(Path.GetFullPath).ToArray();
        if (writes.Any(reads.Contains) || writes.Distinct(CompilationInputSession.PathComparer).Count() != writes.Length)
        {
            options.Diagnostics = null; // Never overwrite an input while reporting a rejected collision.
            throw new ArgumentException("Output and sidecar paths must be distinct and must not overwrite explicit inputs.");
        }
    }

    private static void RejectSnapshotOverwrite(Options options, CompilationInputSession inputs)
    {
        if (new[] { options.Output, options.IrOutput, options.Manifest, options.Diagnostics, options.WriteInputLock }
            .OfType<string>().Any(inputs.ContainsPath))
        {
            options.Diagnostics = null;
            throw new ArgumentException("Output or sidecar would overwrite a snapshotted compilation input.");
        }
    }

    private static void TryWriteDiagnostics(string path, IEnumerable<Diagnostic> diagnostics)
    {
        try { Write(path, JsonSerializer.Serialize(diagnostics, Json)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine("TR0002: Cannot write diagnostics: " + error.Message); }
    }
    private static string RequireOutput(Options options) => options.Output ?? throw new ArgumentException("--out is required.");
    private static void Write(string path, string text) => Write(path, new UTF8Encoding(false).GetBytes(text));
    private static void Write(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, full, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static Options Parse(string[] args)
    {
        var options = new Options { Command = args[0] };
        if (options.Command is not ("compile" or "emit-pe" or "inspect" or "analyze" or "capabilities")) throw new ArgumentException("Unknown command: " + options.Command);
        for (var i = 1; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + args[i - 1]);
            switch (args[i])
            {
                case "--out": case "-o": options.Output = Value(); break;
                case "--target": case "-t": options.Target = Value(); break;
                case "--dispatch": options.Dispatch = Value(); options.DispatchExplicit = true; break;
                case "--ir": options.IrOutput = Value(); break;
                case "--diagnostics": options.Diagnostics = Value(); break;
                case "--reference": case "-r": options.References.Add(Value()); break;
                case "--reference-dir": options.ReferenceDirectories.Add(Value()); break;
                case "--input-lock": options.InputLock = Value(); break;
                case "--write-input-lock": options.WriteInputLock = Value(); break;
                case "--max-input-bytes": options.MaxInputBytes = Positive(Value()); break;
                case "--max-total-input-bytes": options.MaxTotalInputBytes = Positive(Value()); break;
                case "--max-input-files":
                    var count = Positive(Value());
                    if (count > int.MaxValue) throw new ArgumentException("--max-input-files exceeds Int32.");
                    options.MaxInputFiles = (int)count; break;
                case "--bcl":
                    var bcl = Value();
                    if (bcl is not ("portable" or "none")) throw new ArgumentException("--bcl must be portable or none.");
                    options.Bcl = bcl == "portable"; break;
                case "--reference-pack": options.ReferencePack = Value(); break;
                case "--corelib": options.CoreLibrary = Value(); break;
                case "--manifest": options.Manifest = Value(); break;
                case "--library": options.Library = true; break;
                case "--debug": options.Debug = true; break;
                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException("Unknown option: " + args[i]);
                    options.Inputs.Add(args[i]); break;
            }
        }
        return options;
    }
    private static long Positive(string value) => long.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0 ? number :
        throw new ArgumentException("Input limits require positive decimal integers.");

    private sealed class Options
    {
        public string Command { get; init; } = "compile";
        public List<string> Inputs { get; } = [];
        public List<string> References { get; } = [];
        public List<string> ReferenceDirectories { get; } = [];
        public string? InputLock { get; set; }
        public string? WriteInputLock { get; set; }
        public long MaxInputBytes { get; set; } = 128L * 1024 * 1024;
        public long MaxTotalInputBytes { get; set; } = 512L * 1024 * 1024;
        public int MaxInputFiles { get; set; } = 4096;
        public string? Output { get; set; }
        public string? Target { get; set; }
        public string Dispatch { get; set; } = "instruction";
        public bool DispatchExplicit { get; set; }
        public string? IrOutput { get; set; }
        public string? Diagnostics { get; set; }
        public bool Bcl { get; set; }
        public string? ReferencePack { get; set; }
        public string? CoreLibrary { get; set; }
        public string? Manifest { get; set; }
        public bool Library { get; set; }
        public bool Debug { get; set; }
    }
    private const string Help = """
        Transpiler — Roslyn / PE-CIL to JavaScript, Python and bounded C++20

        dotnet Transpiler.Cli.dll compile <source.cs ... | assembly.dll> --target js|py|native-std --out file
        dotnet Transpiler.Cli.dll emit-pe <source.cs ...> --out application.dll
        dotnet Transpiler.Cli.dll inspect <assembly.dll> [--out metadata.json]
        dotnet Transpiler.Cli.dll analyze <source.cs ... | assembly.dll> [--out analysis.json]
        dotnet Transpiler.Cli.dll capabilities [--out capabilities.json]

        Options: --library, --debug, --reference path.dll (repeatable),
                 --ir analysis.json, --diagnostics diagnostics.json, --manifest provenance.json,
                 --bcl portable|none, --reference-pack directory, --corelib implementation.dll,
                 --dispatch instruction|block|ssa,
                 --reference-dir directory (repeatable; exact offline dependency closure),
                 --write-input-lock inputs.json OR --input-lock inputs.json (compile only),
                 --max-input-bytes N, --max-total-input-bytes N, --max-input-files N

        native-std: C++20 integral libraries only (--library); always SSA; no managed object runtime.
        C# compilation requires .NET 10 / Roslyn. Generated .mjs and .py programs do not.
        portable-mvp is an explicit subset, not universal CLI or .NET library compatibility.
        """;
}
