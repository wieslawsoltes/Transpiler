"""Duration/provider/timer differential and adversarial ownership tests."""
import hashlib
import json
import shutil
import sys


def register(h):
    for debug in (False, True):
        for dispatch in ('instruction', 'ssa'):
            name = f'host/time-services/{"debug" if debug else "release"}/{dispatch}'
            h.record(name, lambda debug=debug, dispatch=dispatch: run(h, debug, dispatch))


def run(h, debug, dispatch):
    d = h.OUT / 'time-services' / ('debug' if debug else 'release') / dispatch
    d.mkdir(parents=True, exist_ok=True)
    source = h.ROOT / 'tests/time/Library.cs'
    pe = d / 'TimeLibrary.dll'
    h.cli('emit-pe', source, '--library', '--out', pe, *(['--debug'] if debug else []))
    results = []
    for target, extension, engine, driver in [('js', 'mjs', 'node', 'javascript.mjs'), ('py', 'py', sys.executable, 'python.py')]:
        output = d / ('library.' + extension)
        flags = ('--library', '--bcl', 'portable', '--dispatch', dispatch, '--target', target)
        manifest = d / (target + '-manifest.json')
        h.cli('compile', pe, *flags, '--out', output, '--manifest', manifest)
        duplicate = d / ('repeat.' + extension)
        h.cli('compile', pe, *flags, '--out', duplicate)
        assert output.read_bytes() == duplicate.read_bytes(), 'Nondeterministic time service emission'
        origins = json.loads(manifest.read_text())['transpiled']
        for name in ('System.TimeSpan::', 'System.TimeProvider::', 'System.Threading.Timer::',
                     'System.Threading.PeriodicTimer', 'System.Threading.Tasks.Task'):
            assert any(name in m['method'] and m['instructions'] > 0 for m in origins), name
        shutil.copyfile(h.ROOT / 'tests/time' / driver, d / driver)
        actual = h.run(engine, d / driver).stdout
        summary = json.loads(actual.splitlines()[-1])
        assert summary['cases'] == summary['passed'] == 22, actual
        results.append(dict(target=target, scenarios=22, sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
    # Run the same C# library against CoreCLR, including the pathological custom providers.
    oracle = d / 'Oracle.cs'
    oracle.write_text(source.read_text() + '''
public static class TimeOracle {
 public static async System.Threading.Tasks.Task Main() {
  TimeKernel.Reset(); TimeKernel.InlineDelay(); System.Console.WriteLine(await TimeKernel.AwaitWait());
  System.Console.WriteLine(TimeKernel.ProviderDisposals());
  TimeKernel.Reset(); TimeKernel.InlineTimeout(); System.Console.WriteLine(await TimeKernel.AwaitWait());
  System.Console.WriteLine(TimeKernel.ProviderDelayTicks()); System.Console.WriteLine(TimeKernel.ProviderDisposals());
  TimeKernel.Reset(); TimeKernel.ProviderCompletesInput(); System.Console.WriteLine(await TimeKernel.AwaitWait());
  System.Console.WriteLine(TimeKernel.ProviderDisposals());
  TimeKernel.Reset(); System.Console.WriteLine(TimeKernel.ThrowingProvider());
  TimeKernel.Reset(); TimeKernel.CustomCancellation(); System.Console.WriteLine(TimeKernel.ProviderDelayTicks());
  System.Console.WriteLine(TimeKernel.TryResetSource()); TimeKernel.ProviderFire();
  System.Console.WriteLine(TimeKernel.SourceCanceled()); System.Console.WriteLine(TimeKernel.ProviderDisposals()); TimeKernel.CloseSource();
  TimeKernel.Reset(); System.Console.WriteLine(TimeKernel.NativeTimerProviderReset());
  System.Console.WriteLine(await TimeKernel.Delay(3));
  System.Console.WriteLine(await TimeKernel.TimedWait()); System.Console.WriteLine(TimeKernel.InputCompleted());
  System.Console.WriteLine(await TimeKernel.DurationCancellation()); System.Console.WriteLine(await TimeKernel.NativePeriodic());
 }
}
''')
    oracle_pe = d / 'Oracle.dll'
    h.cli('emit-pe', oracle, '--out', oracle_pe, *(['--debug'] if debug else []))
    actual = h.run('dotnet', oracle_pe).stdout
    expected = '42\n1\n-2\n100000\n1\n73\n1\nTrue\n109999\nFalse\nTrue\n1\nFalse\n42\n9\nFalse\n11\n3\n'
    assert actual == expected, f'CoreCLR contract differs: {actual!r}'
    return dict(executions=results, coreClrOracle=actual)
