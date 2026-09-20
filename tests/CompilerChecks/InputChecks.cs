using System.Security.Cryptography;
using Transpiler.Core;
using Transpiler.Frontend.Roslyn;

internal static class InputChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool condition) { if (!condition) throw new InvalidOperationException("Input invariant " + (count + 1)); count++; }
        void Reject(string code, Action action)
        {
            try { action(); }
            catch (CompilationException error) when (error.Diagnostics.Any(d => d.Code == code)) { count++; return; }
            throw new InvalidOperationException("Expected " + code);
        }
        void Canceled(Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { count++; return; }
            throw new InvalidOperationException("Expected cancellation");
        }
        var directory = Path.Combine(Path.GetTempPath(), "transpiler-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "input.dll"); var second = Path.Combine(directory, "other.dll");
            File.WriteAllBytes(path, [1, 2, 3, 4]); File.WriteAllBytes(second, [5, 6, 7, 8]);
            var session = new CompilationInputSession(); var original = session.Read(path);
            Check(original.Sha256 == Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })).ToLowerInvariant());
            File.WriteAllBytes(path, [9, 10, 11, 12]);
            Check(ReferenceEquals(original, session.Read(Path.Combine(directory, ".", "input.dll"))));
            Check(session.FileCount == 1 && session.BytesRead == 4);
            var copy = original.Content.ToArray(); copy[0] = 100;
            Check(original.Content[0] == 1);
            Check(new CompilationInputSession().Read(path).Sha256 != original.Sha256);
            Check(new CompilationInputSession(new(4, 4, 1)).Read(path).Content.Length == 4);
            Reject("TR3300", () => new CompilationInputSession(new(3, 100, 3)).Read(path));
            var total = new CompilationInputSession(new(4, 6, 2)); total.Read(path);
            Reject("TR3300", () => total.Read(second));
            var files = new CompilationInputSession(new(4, 100, 1)); files.Read(path);
            Reject("TR3300", () => files.Read(second));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var interrupted = new CompilationInputSession(cancellationToken: canceled.Token);
            Canceled(() => interrupted.Read(path)); Check(interrupted.BytesRead == 0 && interrupted.FileCount == 0);
            Canceled(() => AssemblyImporter.Read([], cancellationToken: canceled.Token));
            var model = new AssemblyModel("Empty", 0, [], [], []);
            Canceled(() => CompilerAnalysis.Analyze(model, canceled.Token));
            Canceled(() => GenericSpecializer.Expand(model, cancellationToken: canceled.Token));
            Canceled(() => StackSsa.Prepare(new(model, [], []), canceled.Token));
            var assemblyFile = session.Read(typeof(InputChecks).Assembly.Location);
            var header = AssemblyHeader.Read(assemblyFile);
            Check(header.Identity.Name == "CompilerChecks" && header.References.Length > 0 && !header.IsReferenceAssembly);
            using var mid = new CancellationTokenSource();
            Canceled(() => AssemblyImporter.Read(assemblyFile.Content.ToArray(), _ => { mid.Cancel(); return true; }, cancellationToken: mid.Token));
            Reject("TR3030", () => AssemblyHeader.Read(original));
            var hash = new string('a', 64); var otherHash = new string('b', 64);
            var input = new AssemblyInput("App, Version=1.0.0.0", hash);
            var compiler = new AssemblyInput("compiler", otherHash);
            var a = CompilationInputLock.Create("javascript", "instruction", "none", [input], [], [compiler]);
            var text = a.Serialize();
            Check(!text.Contains(directory, StringComparison.Ordinal));
            CompilationInputLock.Parse(text).Verify(a); count++;
            Reject("TR3311", () => a.Verify(a with { Dispatch = "ssa" }));
            Reject("TR3311", () => a.Verify(a with { Target = "python" }));
            Reject("TR3311", () => a.Verify(a with { Bcl = "portable-bcl-v1" }));
            Reject("TR3311", () => a.Verify(a with { Assemblies = [input with { Sha256 = otherHash }] }));
            Reject("TR3311", () => a.Verify(a with { ReferencePack = [compiler] }));
            Reject("TR3311", () => a.Verify(a with { Toolchain = [compiler with { Sha256 = hash }] }));
            Reject("TR3310", () => CompilationInputLock.Parse(text.Replace("\"schema\": 1", "\"schema\": 2", StringComparison.Ordinal)));
            Reject("TR3310", () => CompilationInputLock.Parse(text.Replace("\"schema\": 1", "\"schema\": 1, \"schema\": 1", StringComparison.Ordinal)));
            Reject("TR3310", () => CompilationInputLock.Parse(text.Replace("\"schema\": 1", "\"schema\": 1, \"unknown\": true", StringComparison.Ordinal)));
            Reject("TR3310", () => CompilationInputLock.Parse("{}"));
            Reject("TR3310", () => CompilationInputLock.Parse("null"));
            Reject("TR3310", () => (a with { Assemblies = [input, input] }).Serialize());
            Reject("TR3310", () => (a with { Assemblies = [input with { Sha256 = "bad" }] }).Serialize());
            Reject("TR3310", () => (a with { Toolchain = [] }).Serialize());
            var b = a with { Assemblies = [input, new("Other", otherHash)] };
            (b with { Assemblies = b.Assemblies.Reverse().ToArray() }).Verify(b); count++;
            Check(session.ContainsPath(path) && !session.ContainsPath(Path.Combine(directory, "missing")));
            var root = new AssemblyModel("Root", 0, [], [], []) { Inputs = [new("Root", hash)] };
            var contract = new AssemblyIdentity("System.Runtime", "10.0.0.0", "neutral", "b03f5f7f11d50a3a");
            Check(AssemblyClosure.IsFrameworkContract(contract));
            Check(!AssemblyClosure.IsFrameworkContract(contract with { PublicKeyToken = "null" }));
            Check(!AssemblyClosure.IsFrameworkContract(contract with { Name = "System.Unreviewed" }));
            var resolved = AssemblyClosure.Resolve(root with { References = [contract] }, [], [directory], session);
            Check(resolved.Dependencies.Length == 0 && resolved.Bindings.Single().Kind == "framework-contract");
            Reject("TR3031", () => AssemblyClosure.Resolve(root with { References = [contract with { PublicKeyToken = "null" }] }, [], [directory], session));
            foreach (var badName in new[] { "../Outside", "..\\Outside", "/absolute", "C:device", "trailing.", "trailing " })
                Reject("TR3033", () => AssemblyClosure.Resolve(root with { References = [contract with { Name = badName }] }, [], [directory], session));
            var self = AssemblyClosure.Resolve(root with { References = [root.Identity] }, [], [directory], session);
            Check(self.Bindings.Single().Sha256 == hash && self.Dependencies.Length == 0);
            Canceled(() => AssemblyClosure.Resolve(root, [], [directory], interrupted));
            Reject("TR3034", () => AssemblyClosure.Resolve(root, [], Enumerable.Range(0, 257).Select(i => Path.Combine(directory, i.ToString())), session));
            var tooMany = root with { References = Enumerable.Range(0, AssemblyClosure.MaxReferences + 1)
                .Select(i => contract with { Version = "10.0." + i + ".0" }).ToArray() };
            Reject("TR3034", () => AssemblyClosure.Resolve(tooMany, [], [directory], session));
            using var frontendCancel = new CancellationTokenSource(); frontendCancel.Cancel();
            Canceled(() => RoslynFrontend.Compile([new("A.cs", "class A {}")], inputs: new(cancellationToken: frontendCancel.Token)));
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("Input snapshots and locks: " + count + " passed");
    }
}
