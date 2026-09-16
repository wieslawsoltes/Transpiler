"""Reference-instruction versus basic-block emission: output equality and deterministic size metrics."""
import json
import sys

def register(h):
    h.record('compiler/block-dispatch', lambda: run(h))

def run(h):
    fixtures = [
        ('programs', 'ControlFlow'), ('programs', 'Exceptions'), ('programs', 'ValueStorage'), ('programs', 'NumericEdges'),
        ('bcl', 'CancellationComposition'), ('bcl', 'RectangularArrays'), ('bcl', 'ArrayConversions'),
        ('bcl', 'TaskComposition'), ('bcl', 'LinqCollections'), ('bcl', 'ValueTasks'),
        ('bcl', 'AsyncStreams'), ('bcl', 'ValueTaskSources')]
    metrics = []
    for folder, name in fixtures:
        for debug in (False, True):
            d = h.OUT / 'block-dispatch' / (name + ('-debug' if debug else '-release'))
            d.mkdir(parents=True, exist_ok=True)
            pe = d / (name + '.dll')
            h.cli('emit-pe', h.ROOT/'tests'/folder/(name+'.cs'), '--out', pe, *(['--debug'] if debug else []))
            oracle = h.run('dotnet', pe, expected=None)
            bcl = ['--bcl', 'portable'] if folder == 'bcl' else []
            for target, ext, engine in [('js','mjs','node'),('py','py',sys.executable)]:
                records = {}
                for mode in ('instruction', 'block'):
                    path = d/(mode+'.'+ext)
                    response = h.cli('compile', pe, '--target', target, '--out', path, '--dispatch', mode, *bcl)
                    result = h.run(engine, path, expected=None)
                    assert (result.returncode, result.stdout) == (oracle.returncode, oracle.stdout), f'{name}/{mode}/{target}: {result.stderr}'
                    records[mode] = json.loads(response.stdout)
                assert records['block']['DispatchCaseCount'] < records['instruction']['DispatchCaseCount']
                assert records['block']['bytes'] < records['instruction']['bytes']
                if not debug:
                    repeat = d/('repeat.'+ext)
                    h.cli('compile', pe, '--target', target, '--out', repeat, '--dispatch', 'block', *bcl)
                    assert repeat.read_bytes() == (d/('block.'+ext)).read_bytes()
                metrics.append(dict(fixture=name, debug=debug, target=target,
                    instructionCases=records['instruction']['DispatchCaseCount'], blockCases=records['block']['DispatchCaseCount'],
                    instructionBytes=records['instruction']['bytes'], blockBytes=records['block']['bytes']))
    return dict(programConfigurations=len(fixtures)*2, targetPairs=len(metrics), comparisons=metrics,
                optimization='dispatch coalescing only; semantic helpers and fault offsets retained')
