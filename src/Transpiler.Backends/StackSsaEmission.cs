using System.Globalization;
using Transpiler.Core;

namespace Transpiler.Backends;

internal sealed class StackSsaEmission
{
    private readonly StackSsaGraph _graph;
    private readonly bool _python;
    private readonly Dictionary<int, SsaOperation> _operations;
    private readonly Dictionary<int, SsaBlock> _blocks;
    private readonly Dictionary<int, SsaBlock> _owners;
    private readonly HashSet<int> _live;
    private readonly HashSet<int> _active;
    private SsaOperation _operation = null!;
    private int _pop;

    public StackSsaEmission(StackSsaGraph graph, bool python)
    {
        _graph = graph; _python = python;
        _operations = graph.Blocks.SelectMany(b => b.Operations).ToDictionary(o => o.Instruction.Offset);
        _blocks = graph.Blocks.ToDictionary(b => b.Start);
        _owners = graph.Blocks.SelectMany(b => b.Operations.Select(o => (o.Instruction.Offset, Block: b))).ToDictionary(x => x.Offset, x => x.Block);
        _live = graph.LiveValues.ToHashSet(); _active = graph.ActiveOffsets.ToHashSet();
    }
    public void Begin(int offset) { _operation = _operations[offset]; _pop = _operation.Inputs.Length; }
    public bool Active => _active.Contains(_operation.Instruction.Offset);
    public string Pop() => _pop > 0 ? Value(_operation.Inputs[--_pop]) : throw new InvalidOperationException("SSA operand underflow.");
    public string Peek() => Value(_operation.Inputs[^1]);
    public string Push(string expression) => _operation.Result is { } result && _live.Contains(result) ? $"v{result} = {expression}" : expression;
    private string Value(int id)
    {
        if (!_graph.Constants.TryGetValue(id, out var constant)) return "v" + id.ToString(CultureInfo.InvariantCulture);
        return constant.Value.ToString(CultureInfo.InvariantCulture) + (constant.Kind == "i8" && !_python ? "n" : "");
    }
    public string Declarations => string.Join(", ", _graph.LiveValues.Select(v => "v" + v)
        .Concat(Enumerable.Range(0, _graph.Blocks.Max(b => b.Parameters.Length)).Select(i => "p" + i)));
    public IEnumerable<string> Transfers()
    {
        var block = _owners[_operation.Instruction.Offset];
        foreach (var target in block.Successors)
        {
            var parameters = _blocks[target].Parameters;
            var moves = parameters.Select((value, slot) => (Value: value, Slot: slot)).Where(x => _live.Contains(x.Value)).ToArray();
            if (moves.Length == 0) continue;
            // Stage ALL incoming values before updating phi destinations, including self-loop cycles.
            var statements = moves.Select(x => $"p{x.Slot} = {Value(block.ExitValues[x.Slot])}")
                .Concat(moves.Select(x => $"v{x.Value} = p{x.Slot}"));
            var body = string.Join("; ", statements);
            yield return _python ? $"if pc == {target}: {body}" : $"if (pc === {target}) {{ {body}; }}";
        }
    }
}
