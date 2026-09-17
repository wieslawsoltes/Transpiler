namespace Transpiler.Backends;

/// <summary>
/// Turn an emitted source body into a live activation with a reentrant filter entry point.
/// Locals/arguments are shared, evaluation stacks and temporary operands are not. This is source
/// control flow, not an instruction interpreter. Used only by modules requiring two-pass search.
/// </summary>
internal static class ExceptionFrameEmitter
{
    public static string Wrap(string text, bool python)
    {
        var marker = python ? "    while True:" : "    while (true) {";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing emitted loop boundary.");
        var header = text[..start];
        var loop = text[start..];
        if (python)
        {
            header = header.Replace("    s = []\n", "", StringComparison.Ordinal)
                .Replace("    pc = 0\n", "", StringComparison.Ordinal)
                .Replace("flow = CliFlow(", "flow = CliSearchFlow(", StringComparison.Ordinal);
            loop = loop.Replace("        except CliError as error:\n", "        except CliError as error:\n            if filtering: raise\n", StringComparison.Ordinal);
            var nested = string.Join('\n', loop.TrimEnd('\n').Split('\n').Select(l => "    " + l));
            return header + "    def run(pc, s, filtering=False):\n" + nested + "\n" +
                "    flow.evaluate = lambda pc, value: run(pc, [value], True)\n" +
                "    R.push_frame(flow)\n    try:\n        return run(0, [])\n    finally:\n        R.pop_frame(flow)\n";
        }
        header = header.Replace("const s = [], l = ", "const l = ", StringComparison.Ordinal)
            .Replace("    let pc = 0, x, y, z;\n", "", StringComparison.Ordinal)
            .Replace("new CliFlow(", "new CliSearchFlow(", StringComparison.Ordinal);
        var end = loop.LastIndexOf('}');
        loop = loop[..end]; // Remove the original outer method's closing brace.
        loop = loop.Replace("if (!(error instanceof CliError)) throw error;", "if (!(error instanceof CliError) || filtering) throw error;", StringComparison.Ordinal);
        return header + "    function run(pc, s, filtering = false) {\n        let x, y, z;\n" +
            string.Join('\n', loop.TrimEnd('\n').Split('\n').Select(l => "    " + l)) + "\n    }\n" +
            "    flow.evaluate = (pc, value) => run(pc, [value], true);\n    R.push_frame(flow);\n" +
            "    try { return run(0, []); } finally { R.pop_frame(flow); }\n}\n";
    }
}
