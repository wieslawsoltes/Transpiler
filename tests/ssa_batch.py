"""SSA differential coverage, including persisted IL not normally produced by safe C#."""
import json
import re
import sys


def register(h):
    for folder in ('programs', 'bcl'):
        for source in sorted((h.ROOT / 'tests' / folder).glob('*.cs')):
            for debug in (False, True):
                name = f"ssa/{folder}/{source.stem}/{'debug' if debug else 'release'}"
                h.record(name, lambda source=source, debug=debug, folder=folder: differential(h, source, debug, folder == 'bcl'))
    h.record('ssa/raw-il-and-invariants', lambda: raw(h))


def compare(h, pe, directory, bcl=False, arguments=()):
    oracle = h.run('dotnet', pe, *arguments, expected=None)
    assert oracle.returncode in (0, 7), oracle.stderr
    metrics = []
    for target, extension, engine in [('js', 'mjs', 'node'), ('py', 'py', sys.executable)]:
        output = directory / ('ssa.' + extension)
        flags = ['--bcl', 'portable'] if bcl else []
        response = h.cli('compile', pe, '--target', target, '--dispatch', 'ssa', '--out', output, *flags)
        actual = h.run(engine, output, *arguments, expected=None)
        assert (actual.returncode, actual.stdout) == (oracle.returncode, oracle.stdout), f'{target}: {actual.stderr}\n{actual.stdout!r} != {oracle.stdout!r}'
        repeat = directory / ('repeat.' + extension)
        h.cli('compile', pe, '--target', target, '--dispatch', 'ssa', '--out', repeat, *flags)
        assert output.read_bytes() == repeat.read_bytes(), 'nondeterministic SSA emission'
        text = output.read_text()
        bodies = re.findall(r'(?m)^(?:#|//)[^\n]*\[stack-ssa\]\n(.*?)(?=\n(?:#|//) |\Z)', text, re.S)
        for body in bodies:
            assert not any(op in body for op in ('s.pop()', 's.push(', 's.append(')), 'dynamic evaluation stack in SSA function'
        record = json.loads(response.stdout)
        assert len(bodies) == record['SsaMethodCount']
        metrics.append(dict(target=target, optimizedMethods=record['SsaMethodCount'],
            eliminatedInstructions=record['SsaEliminatedInstructions'], fallbacks=record['SsaFallbacks'], bytes=record['bytes']))
    return dict(executions=metrics)


def differential(h, source, debug, bcl):
    d = h.OUT / 'ssa' / source.parent.name / (source.stem + ('-debug' if debug else '-release'))
    d.mkdir(parents=True, exist_ok=True)
    pe = d / (source.stem + '.dll')
    h.cli('emit-pe', source, '--out', pe, *(['--debug'] if debug else []))
    return compare(h, pe, d, bcl, ('hello', 'unicode-\u03bb'))


def raw(h):
    d = h.OUT / 'ssa-raw'; d.mkdir(parents=True, exist_ok=True)
    checks = h.ROOT / 'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll'
    invariants = h.run('dotnet', checks, '--ssa').stdout.strip()
    pe = d / 'SsaFixture.dll'
    h.run('dotnet', checks, '--emit-ssa', pe)
    result = compare(h, pe, d)
    assert all(x['optimizedMethods'] >= 132 and x['eliminatedInstructions'] >= 3980 for x in result['executions'])
    for target, extension, engine in [('js', 'mjs', 'node'), ('py', 'py', sys.executable)]:
        path = d / ('instruction.' + extension)
        response = h.cli('compile', pe, '--target', target, '--dispatch', 'instruction', '--out', path)
        assert h.run(engine, path).stdout == h.run('dotnet', pe).stdout
        assert json.loads(response.stdout)['bytes'] > (d / ('ssa.' + extension)).stat().st_size
    h.cli('analyze', pe, '--dispatch', 'ssa', '--out', d / 'ssa.json')
    graph = json.loads((d / 'ssa.json').read_text())
    loop = next(m['ssa'] for m in graph['methods'] if m['method']['reference']['name'] == 'Loop')
    assert any(len(phi['incoming']) > 1 for phi in loop['phis'])
    return dict(**result, invariants=invariants, randomIntegerMethods=128, oracle='CoreCLR persisted hand-authored IL')
