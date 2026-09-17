"""Native host stream lifecycle, exact managed protocol provenance, and CoreCLR output equivalence."""
import json
import shutil
import sys


def register(h):
    for debug in (False, True):
        for dispatch in ('instruction', 'block'):
            name = 'interop/native-streams/' + ('debug' if debug else 'release') + '/' + dispatch
            h.record(name, lambda debug=debug, dispatch=dispatch: run(h, debug, dispatch))


def run(h, debug, dispatch):
    source = h.ROOT / 'tests/host-streams'
    directory = h.OUT / 'host-streams' / (('debug' if debug else 'release') + '-' + dispatch)
    directory.mkdir(parents=True, exist_ok=True)
    implementation = directory / 'Library.dll'
    flags = ['--debug'] if debug else []
    h.cli('emit-pe', source / 'Library.cs', '--library', '--out', implementation, *flags)
    oracle = directory / 'Oracle.dll'
    h.cli('emit-pe', source / 'Oracle.cs', '--reference', implementation, '--out', oracle, *flags)
    expected = h.run('dotnet', oracle).stdout
    observed = []
    for target, extension, host, driver in [('js', 'mjs', 'node', 'javascript.mjs'), ('py', 'py', sys.executable, 'python.py')]:
        output = directory / ('library.' + extension)
        manifest = directory / (target + '.json')
        h.cli('compile', implementation, '--bcl', 'portable', '--dispatch', dispatch,
              '--target', target, '--out', output, '--manifest', manifest)
        shutil.copyfile(source / driver, directory / driver)
        shutil.copyfile(source / ('oracle.' + extension), directory / ('oracle.' + extension))
        result = json.loads(h.run(host, directory / driver).stdout)
        assert result['cases'] >= 22 and result['activeStreams'] == 0, result
        actual = h.run(host, directory / ('oracle.' + extension)).stdout
        assert actual == expected, (target, actual, expected)
        provenance = json.loads(manifest.read_text())
        cursor = [m for m in provenance['transpiled'] if 'Transpiler.Bcl.Interop.StreamCursor`1<' in m['method']]
        assert len(cursor) >= 12 and all(m['assembly'] == 'Transpiler.Bcl' and m['instructions'] > 0 for m in cursor)
        for member in ('::Open(', '::StartMove(', '::FinishMove(', '::StartDispose(', '::FinishDispose(', '::Release('):
            assert any(member in m['method'] for m in cursor), member
        repeat = directory / ('repeat.' + extension)
        h.cli('compile', implementation, '--bcl', 'portable', '--dispatch', dispatch, '--target', target, '--out', repeat)
        assert repeat.read_bytes() == output.read_bytes()
        observed.append(dict(target=target, lifecycleCases=result['cases'], cursorMethods=len(cursor)))
    return dict(dispatch=dispatch, debug=debug, coreClrStdout=expected, hosts=observed)
