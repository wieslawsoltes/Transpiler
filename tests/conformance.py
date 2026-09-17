#!/usr/bin/env python3
"""Differential oracle: Roslyn emits one DLL; CoreCLR, generated JS, and generated Python execute it.
Every child has a timeout. Negative cases must fail compilation without creating target output.
Only the Python standard library is required. Run after `dotnet build Transpiler.slnx -c Release`.
"""
from __future__ import annotations
from concurrent.futures import ThreadPoolExecutor
from collections.abc import Callable
from threading import Lock
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts' / 'conformance'
CLI = ROOT / 'src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll'
ENV = dict(os.environ, DOTNET_SYSTEM_GLOBALIZATION_INVARIANT='1', LC_ALL='C.UTF-8', PYTHONUTF8='1')
RESULTS: list[dict] = []
CASES: list[tuple[str, Callable[[], dict | None]]] = []
PRINT_LOCK = Lock()


def run(*args: object, expected: int | None = 0) -> subprocess.CompletedProcess:
    command = [str(arg) for arg in args]
    result = subprocess.run(command, cwd=ROOT, env=ENV, capture_output=True, text=True, encoding='utf-8', timeout=40)
    if expected is not None and result.returncode != expected:
        raise AssertionError(f'{command}\nexit={result.returncode}\nstdout={result.stdout}\nstderr={result.stderr}')
    return result


def cli(*args: object, expected: int | None = 0) -> subprocess.CompletedProcess:
    return run('dotnet', CLI, *args, expected=expected)


def record(name: str, action: Callable[[], dict | None]) -> None:
    if any(existing == name for existing, _ in CASES): raise ValueError('Duplicate test case: ' + name)
    CASES.append((name, action))


def execute(case: tuple[str, Callable[[], dict | None]]) -> dict:
    name, action = case
    start = time.perf_counter()
    try:
        detail = action() or {}
        result = dict(name=name, passed=True, seconds=round(time.perf_counter() - start, 4), **detail)
        with PRINT_LOCK: print('PASS', name, flush=True)
    except Exception as error:
        result = dict(name=name, passed=False, seconds=round(time.perf_counter() - start, 4), error=str(error))
        with PRINT_LOCK: print('FAIL', name, str(error), flush=True)
    return result


def differential(source: Path, debug: bool, bcl: bool = False) -> dict:
    directory = OUT / (source.stem + ('-debug' if debug else '-release'))
    directory.mkdir(parents=True, exist_ok=True)
    pe = directory / (source.stem + '.dll')
    flags = ['--debug'] if debug else []
    cli('emit-pe', source, '--out', pe, *flags)
    args = ['hello', 'unicode-\u03bb']
    oracle = run('dotnet', pe, *args, expected=None)
    if oracle.returncode not in (0, 7): raise AssertionError('Oracle program failed: ' + oracle.stderr)
    executions = []
    for target, extension, runtime in [('js', '.mjs', 'node'), ('py', '.py', sys.executable)]:
        output = directory / ('program' + extension)
        cli('compile', pe, '--target', target, '--out', output, '--ir', directory / 'analysis.json', *(['--bcl', 'portable'] if bcl else []))
        actual = run(runtime, output, *args, expected=None)
        if (actual.returncode, actual.stdout) != (oracle.returncode, oracle.stdout):
            raise AssertionError(f'{target} differs from .NET\nexpected exit={oracle.returncode}, stdout={oracle.stdout!r}\nactual exit={actual.returncode}, stdout={actual.stdout!r}\nstderr={actual.stderr}')
        duplicate = directory / ('repeat' + extension)
        cli('compile', pe, '--target', target, '--out', duplicate, *(['--bcl', 'portable'] if bcl else []))
        if duplicate.read_bytes() != output.read_bytes(): raise AssertionError(target + ' emission is not deterministic')
        executions.append(dict(target=target, bytes=output.stat().st_size, sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
    return dict(exitCode=oracle.returncode, stdout=oracle.stdout, executions=executions)


def negative(source: Path) -> dict:
    directory = OUT / ('negative-' + source.stem)
    directory.mkdir(parents=True, exist_ok=True)
    codes = set()
    for target, extension in [('js', '.mjs'), ('py', '.py')]:
        output, diagnostics = directory / ('program' + extension), directory / (target + '.json')
        output.unlink(missing_ok=True)
        result = cli('compile', source, '--target', target, '--out', output, '--diagnostics', diagnostics, expected=None)
        if result.returncode == 0 or output.exists(): raise AssertionError('Unsupported source emitted an artifact')
        payload = json.loads(diagnostics.read_text())
        if not payload or not all(item['code'].startswith('TR') for item in payload):
            raise AssertionError('Expected compiler capability diagnostics, not invalid C# source: ' + str(payload))
        codes.update(item['code'] for item in payload)
    return dict(diagnosticCodes=sorted(codes))


def library() -> dict:
    directory = OUT / 'library'
    directory.mkdir(parents=True, exist_ok=True)
    js, py = directory / 'library.mjs', directory / 'library.py'
    for target, path in [('js', js), ('py', py)]:
        cli('compile', ROOT / 'samples/Library.cs', '--library', '--target', target, '--out', path)
    js_driver = directory / 'driver.mjs'
    js_driver.write_text("import {invoke} from './library.mjs';\nconsole.log(invoke('Kernel::Add', [9007199254740993n, 2n]).toString());\nconsole.log(invoke('Kernel::Gcd', [84,30]));\nconsole.log(invoke('Kernel::Echo', ['hello']));\nconsole.log(invoke('Kernel::Positive',[3]));\n", encoding='utf-8')
    py_driver = directory / 'driver.py'
    py_driver.write_text("from library import invoke\nprint(invoke('Kernel::Add', [9007199254740993,2]))\nprint(invoke('Kernel::Gcd', [84,30]))\nprint(invoke('Kernel::Echo', ['hello']))\nprint(str(invoke('Kernel::Positive',[3])).lower())\n", encoding='utf-8')
    expected = '9007199254740995\n6\nhello\ntrue\n'
    for runtime, path in [('node', js_driver), (sys.executable, py_driver)]:
        actual = run(runtime, path)
        if actual.stdout != expected: raise AssertionError(actual.stdout)
    return dict(stdout=expected)


def malformed() -> dict:
    path = OUT / 'invalid.dll'
    path.write_bytes(b'not a PE image')
    result = cli('inspect', path, expected=None)
    if result.returncode != 1 or 'TR1001' not in result.stderr: raise AssertionError(result.stderr)
    return dict(diagnostic='TR1001')


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'report.json').unlink(missing_ok=True)
    CASES.clear(); RESULTS.clear()
    if not CLI.exists(): raise SystemExit('Build the solution before running conformance tests.')
    for source in sorted((ROOT / 'tests/programs').glob('*.cs')) + [ROOT / 'samples/Hello.cs']:
        for debug in (False, True):
            record(source.stem + ('/debug' if debug else '/release'), lambda source=source, debug=debug: differential(source, debug))
    for source in sorted((ROOT / 'tests/bcl').glob('*.cs')):
        for debug in (False, True):
            record('bcl/' + source.stem + ('/debug' if debug else '/release'), lambda source=source, debug=debug: differential(source, debug, True))
    for source in sorted((ROOT / 'tests/negative').glob('*.cs')):
        record('reject/' + source.stem, lambda source=source: negative(source))
    record('library/host-interop', library)
    record('metadata/malformed-pe', malformed)
    import extended
    extended.register(sys.modules[__name__])
    selected_filter = os.environ.get('TRANSPILER_TEST_FILTER', '')
    selected = [case for case in CASES if selected_filter in case[0]]
    if not selected:
        print('No conformance cases selected; refusing an empty success.', file=sys.stderr)
        return 2
    try: workers = int(os.environ.get('TRANSPILER_TEST_WORKERS', '1'))
    except ValueError: raise SystemExit('TRANSPILER_TEST_WORKERS must be an integer from 1 through 8.')
    if not 1 <= workers <= 8: raise SystemExit('TRANSPILER_TEST_WORKERS must be from 1 through 8.')
    # Each registered case owns a distinct artifact directory. Results retain registration order,
    # irrespective of completion order, and no case is silently dropped after another fails.
    with ThreadPoolExecutor(max_workers=min(workers, len(selected))) as executor:
        RESULTS.extend(executor.map(execute, selected))
    cli('capabilities', '--out', OUT / 'capabilities.json')
    report = dict(schema=1, workers=workers, filter=selected_filter, registeredCases=len(CASES), selectedCases=len(selected),
                  complete=not selected_filter and len(selected)==len(CASES), platform=platform.platform(), python=platform.python_version(),
                  node=run('node', '--version').stdout.strip(), dotnet=run('dotnet', '--version').stdout.strip(),
                  passed=sum(r['passed'] for r in RESULTS), failed=sum(not r['passed'] for r in RESULTS), results=RESULTS)
    (OUT / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=True) + '\n', encoding='utf-8')
    print(f"Conformance: {report['passed']} passed, {report['failed']} failed", flush=True)
    return int(report['failed'] != 0)


if __name__ == '__main__':
    raise SystemExit(main())
