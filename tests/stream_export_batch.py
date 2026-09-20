"""Generalized stream discovery, asynchronous acquisition ownership, and unchanged-PE oracles."""
import hashlib
import json
import shutil
import sys


def register(h):
    h.record('compiler/stream-contract-discovery', lambda: discovery(h))
    for debug in (False, True):
        for dispatch in ('instruction', 'block', 'ssa'):
            h.record('interop/stream-exports/' + ('debug' if debug else 'release') + '/' + dispatch,
                     lambda debug=debug, dispatch=dispatch: run(h, debug, dispatch))


def discovery(h):
    result = h.run('dotnet', h.ROOT / 'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll', '--streams')
    assert 'Stream contracts: 30 passed' in result.stdout, result.stdout
    return dict(assertions=30)


def run(h, debug, dispatch):
    source = h.ROOT / 'tests/stream-exports'
    directory = h.OUT / 'stream-exports' / (('debug' if debug else 'release') + '-' + dispatch)
    directory.mkdir(parents=True, exist_ok=True)
    pe = directory / 'Library.dll'
    flags = ['--debug'] if debug else []
    h.cli('emit-pe', source / 'Library.cs', '--library', '--out', pe, *flags)
    oracle = directory / 'Oracle.dll'
    h.cli('emit-pe', source / 'Oracle.cs', '--reference', pe, '--out', oracle, *flags)
    expected = h.run('dotnet', oracle).stdout
    assert expected == '10\n11\n' * 8 + 'alpha\nbeta\n9007199254740993\n1\n9007199254740995\n2\n1\n10\n', expected
    observations, names = [], None
    for target, extension, host, driver in [('js', 'mjs', 'node', 'javascript.mjs'), ('py', 'py', sys.executable, 'python.py')]:
        output = directory / ('library.' + extension)
        manifest = directory / (target + '.json')
        h.cli('compile', pe, '--bcl', 'portable', '--dispatch', dispatch, '--target', target, '--out', output, '--manifest', manifest)
        shutil.copyfile(source / driver, directory / driver)
        shutil.copyfile(source / ('oracle.' + extension), directory / ('oracle.' + extension))
        result = json.loads(h.run(host, directory / driver).stdout)
        assert result['cases'] == 45 and len(result['names']) == 45, result
        if names is None: names = result['names']
        else: assert names == result['names'], 'Host scenario inventories diverged'
        assert all(result['info'][key] == 0 for key in ('activeStreams', 'activeStreamFactories', 'activeTimers', 'hostWaiters'))
        assert h.run(host, directory / ('oracle.' + extension)).stdout == expected
        data = json.loads(manifest.read_text())
        factory = [m for m in data['transpiled'] if 'Transpiler.Bcl.Interop.StreamFactory`2<' in m['method']]
        assert factory and all(m['assembly'] == 'Transpiler.Bcl' and m['instructions'] > 0 for m in factory)
        for member in ('::Open(', '::OpenTask(', '::OpenValueTask(', '::Finish('):
            assert any(member in m['method'] for m in factory), member
        assert any('::FinishFactory(' in m['method'] and m['instructions'] > 0 for m in data['transpiled'])
        repeat = directory / ('repeat.' + extension)
        h.cli('compile', pe, '--bcl', 'portable', '--dispatch', dispatch, '--target', target, '--out', repeat)
        assert repeat.read_bytes() == output.read_bytes(), 'Non-deterministic generalized stream emission'
        observations.append(dict(target=target, scenarios=result['cases'], factoryMethods=len(factory),
                                 sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
    return dict(debug=debug, dispatch=dispatch, hosts=observations, coreClrStdout=expected)
