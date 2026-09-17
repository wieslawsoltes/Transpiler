"""Clock ABI, deterministic timer races, compiled lifetime checks and CoreCLR boundaries."""
import hashlib
import json
import shutil
import sys


def register(h):
    for runtime, extension in [('node', 'mjs'), (sys.executable, 'py')]:
        h.record('host/clock-unit/' + extension, lambda runtime=runtime, extension=extension: units(h, runtime, extension))
    for dispatch in ('instruction', 'ssa'):
        h.record('host/timers/' + dispatch, lambda dispatch=dispatch: lifecycle(h, dispatch))


def units(h, runtime, extension):
    result = h.run(runtime, h.ROOT / ('tests/timers/clock_unit.' + extension))
    detail = json.loads(result.stdout.splitlines()[-1])
    assert detail['cases'] == detail['passed'] == 13, result.stdout
    return detail


def lifecycle(h, dispatch):
    directory = h.OUT / ('timers-' + dispatch)
    directory.mkdir(parents=True, exist_ok=True)
    source = h.ROOT / 'tests/timers/Library.cs'
    results = []
    for target, extension, runtime, driver in [('js', 'mjs', 'node', 'javascript.mjs'), ('py', 'py', sys.executable, 'python.py')]:
        output = directory / ('library.' + extension)
        flags = ('--library', '--bcl', 'portable', '--target', target, '--dispatch', dispatch)
        h.cli('compile', source, *flags, '--out', output)
        repeat = directory / ('repeat.' + extension)
        h.cli('compile', source, *flags, '--out', repeat)
        assert output.read_bytes() == repeat.read_bytes(), 'Nondeterministic timer emission'
        shutil.copyfile(h.ROOT / ('tests/timers/' + driver), directory / driver)
        result = h.run(runtime, directory / driver)
        detail = json.loads(result.stdout.splitlines()[-1])
        assert detail['cases'] == detail['passed'] == 12, result.stdout
        results.append(dict(target=target, **detail, sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
    # Independently execute real CoreCLR async delays, token identity and iterator-finally behavior.
    oracle = directory / 'Oracle.cs'
    oracle.write_text(source.read_text() + '''
public static class TimerOracle {
 public static async System.Threading.Tasks.Task Main() {
  System.Console.WriteLine(await TimerKernel.Delayed(10));
  System.Console.WriteLine(await TimerKernel.TimedCancel());
  await foreach(var value in TimerKernel.Values(2)) System.Console.WriteLine(value);
  System.Console.WriteLine(TimerKernel.DisposedStreams());
 }
}
''')
    pe = directory / 'Oracle.dll'
    h.cli('emit-pe', oracle, '--out', pe)
    actual = h.run('dotnet', pe).stdout
    assert actual == '42\n9\n11\n22\n1\n', actual
    return dict(executions=results, coreClrOracle=actual)
