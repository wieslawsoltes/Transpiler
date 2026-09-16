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
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help") { Console.WriteLine(Help); return 0; }
            options = Parse(args);
            if (options.Command == "capabilities")
            {
                var text = JsonSerializer.Serialize(new
                {
                    schema = 1, profile = "portable-mvp", targets = new[] { "javascript", "python" },
                    opcodes = CompilerAnalysis.SupportedOpcodes, intrinsics = IntrinsicCatalog.All,
                    limitations = new[] { "single input assembly", "no generics/value-type layouts/interfaces/delegates/async", "no reflection or native interop", "no exception filters", "not a security verifier" }
                }, Json);
                if (options.Output is null) Console.WriteLine(text); else Write(options.Output, text);
                return 0;
            }
            if (options.Inputs.Count == 0) throw new ArgumentException("At least one input is required.");
            var source = options.Inputs.All(p => Path.GetExtension(p).Equals(".cs", StringComparison.OrdinalIgnoreCase));
            if (!source && options.Inputs.Count != 1) throw new ArgumentException("Supply C# source files or exactly one managed assembly.");
            if (options.Inputs.Any(p => !File.Exists(p))) throw new FileNotFoundException("An input file does not exist.");
            ManagedCompilation? managed = null;
            byte[] image;
            if (source)
            {
                var files = options.Inputs.Select(p => new SourceFile(Path.GetFileName(p), File.ReadAllText(p, Encoding.UTF8))).ToArray();
                if (files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != files.Length)
                    throw new ArgumentException("Source file names must be unique within one compilation.");
                managed = RoslynFrontend.Compile(files, Path.GetFileNameWithoutExtension(options.Inputs[0]), options.Library, !options.Debug, options.References);
                image = managed.Pe;
            }
            else image = File.ReadAllBytes(options.Inputs[0]);
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
            var assembly = AssemblyImporter.Read(image);
            if (options.Command == "inspect")
            {
                var text = JsonSerializer.Serialize(assembly, Json);
                if (options.Output is null) Console.WriteLine(text); else Write(options.Output, text);
                return 0;
            }
            var analysis = CompilerAnalysis.Analyze(assembly);
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
                _ => throw new ArgumentException("--target must be javascript (js) or python (py).")
            };
            var generated = SourceEmitter.Emit(analysis, target);
            var destination = RequireOutput(options);
            if (options.IrOutput is not null) Write(options.IrOutput, JsonSerializer.Serialize(analysis, Json));
            Write(destination, generated.Text);
            if (options.Diagnostics is not null) Write(options.Diagnostics, "[]\n");
            Console.WriteLine(JsonSerializer.Serialize(new { output = destination, target = target.ToString(), generated.MethodCount, generated.InstructionCount, bytes = Encoding.UTF8.GetByteCount(generated.Text), profile = "portable-mvp" }));
            return 0;
        }
        catch (CompilationException error)
        {
            foreach (var diagnostic in error.Diagnostics) Console.Error.WriteLine(diagnostic);
            if (options?.Diagnostics is { } path) TryWriteDiagnostics(path, error.Diagnostics);
            return 1;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("TR0001: " + error.Message);
            if (options?.Diagnostics is { } path) TryWriteDiagnostics(path, [new Diagnostic("TR0001", error.Message)]);
            return 2;
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
                case "--ir": options.IrOutput = Value(); break;
                case "--diagnostics": options.Diagnostics = Value(); break;
                case "--reference": case "-r": options.References.Add(Value()); break;
                case "--library": options.Library = true; break;
                case "--debug": options.Debug = true; break;
                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal)) throw new ArgumentException("Unknown option: " + args[i]);
                    options.Inputs.Add(args[i]); break;
            }
        }
        return options;
    }
    private sealed class Options
    {
        public string Command { get; init; } = "compile";
        public List<string> Inputs { get; } = [];
        public List<string> References { get; } = [];
        public string? Output { get; set; }
        public string? Target { get; set; }
        public string? IrOutput { get; set; }
        public string? Diagnostics { get; set; }
        public bool Library { get; set; }
        public bool Debug { get; set; }
    }
    private const string Help = """
        Transpiler — Roslyn / PE-CIL to standalone JavaScript and Python

        dotnet Transpiler.Cli.dll compile <source.cs ... | assembly.dll> --target js|py --out file
        dotnet Transpiler.Cli.dll emit-pe <source.cs ...> --out application.dll
        dotnet Transpiler.Cli.dll inspect <assembly.dll> [--out metadata.json]
        dotnet Transpiler.Cli.dll analyze <source.cs ... | assembly.dll> [--out analysis.json]
        dotnet Transpiler.Cli.dll capabilities [--out capabilities.json]

        Options: --library, --debug, --reference path.dll (repeatable),
                 --ir analysis.json, --diagnostics diagnostics.json

        C# compilation requires .NET 10 / Roslyn. Generated .mjs and .py programs do not.
        portable-mvp is an explicit subset, not universal CLI or .NET library compatibility.
        """;
}
