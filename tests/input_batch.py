"""Offline assembly closure and path-independent input-lock acceptance gates."""
import json
import shutil
import sys


def register(h):
    h.record('compiler/input-snapshots-and-locks', lambda: invariants(h))
    for target, extension, runtime in [('js', 'mjs', 'node'), ('py', 'py', sys.executable)]:
        h.record('inputs/closure-lock/' + target, lambda target=target, extension=extension, runtime=runtime: closure(h, target, extension, runtime))
        h.record('inputs/source-bcl-lock/' + target, lambda target=target, extension=extension: source_lock(h, target, extension))
    h.record('inputs/forwarding-closure', lambda: forwarding(h))


def invariants(h):
    output = h.run('dotnet', h.ROOT / 'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll', '--inputs').stdout.strip()
    assert output.endswith('52 passed'), output
    return dict(invariants=output)


def emit(h, path, text, name, references=(), library=True):
    source = path / (name + '.cs'); source.write_text(text, encoding='utf-8')
    pe = path / (name + '.dll')
    flags = ['--library'] if library else []
    for reference in references: flags += ['--reference', reference]
    h.cli('emit-pe', source, '--out', pe, *flags)
    return pe


def rejected(h, code, root, target, output, *flags):
    output.unlink(missing_ok=True)
    result = h.cli('compile', root, '--target', target, '--out', output, *flags, expected=None)
    assert result.returncode != 0 and code in result.stderr and not output.exists(), (result.stdout, result.stderr)


def closure(h, target, extension, runtime):
    d = h.OUT / ('input-closure-' + target); d.mkdir(parents=True, exist_ok=True)
    dirs = [d / name for name in ('left', 'right', 'duplicates', 'relocated', 'wrong', 'reference', 'corrupt')]
    for directory in dirs: directory.mkdir(exist_ok=True)
    left, right, duplicates, relocated, wrong, reference, corrupt = dirs
    leaf = emit(h, left, 'public static class Leaf { public static int Value() => 41; }', 'Leaf')
    middle = emit(h, right, 'public static class Middle { public static int Value() => Leaf.Value()+1; }', 'Middle', [leaf])
    # A valid metadata cycle: the otherwise-unused reverse call is never executed by the oracle.
    leaf = emit(h, left, 'public static class Leaf { public static int Value()=>41; public static int Reverse()=>Middle.Value(); }', 'Leaf', [middle])
    root = emit(h, d, 'public static class Program { public static void Main() {System.Console.WriteLine(Middle.Value());}}', 'App', [middle, leaf], False)
    output, lock, manifest = d / ('program.' + extension), d / 'inputs.json', d / 'manifest.json'
    flags = ['--reference-dir', left, '--reference-dir', right]
    h.cli('compile', root, '--target', target, '--out', output, *flags, '--write-input-lock', lock, '--manifest', manifest)
    assert h.run(runtime, output).stdout == '42\n'
    initial = output.read_bytes(); recorded = lock.read_bytes()
    payload = json.loads(recorded)
    assert payload['policy'] == 'compilation-input-lock-v1' and len(payload['assemblies']) == 3
    assert str(d) not in recorded.decode() and len(payload['toolchain']) == 7
    report = json.loads(manifest.read_text())
    assert report['inputLock']['verified'] is False
    assert any('Leaf,' in b['requester'] and 'Middle,' in b['requested'] for b in report['dependencyBindings'])
    assert all(b['sha256'] is not None for b in report['dependencyBindings'] if b['kind'] == 'implementation')
    # Alternate path order, same content at duplicate paths, and an unrelated malformed DLL are harmless.
    shutil.copy2(leaf, duplicates / 'Leaf.dll'); (left / 'Unrelated.dll').write_bytes(b'not a PE')
    h.cli('compile', root, '--target', target, '--out', output, '--reference-dir', right, '--reference-dir', duplicates,
          '--reference-dir', left, '--input-lock', lock, '--manifest', manifest)
    assert output.read_bytes() == initial and json.loads(manifest.read_text())['inputLock']['verified'] is True
    assert lock.read_bytes() == recorded
    # Relocate the whole implementation closure. No source/lock rebasing is required.
    for path in (root, middle, leaf): shutil.copy2(path, relocated / path.name)
    h.cli('compile', relocated / root.name, '--target', target, '--out', output,
          '--reference-dir', relocated, '--input-lock', lock)
    assert output.read_bytes() == initial
    h.cli('compile', root, '--target', target, '--out', output, '--reference', middle, '--reference', leaf, '--input-lock', lock)
    assert output.read_bytes() == initial
    optimized, optimized_lock = d / ('ssa.' + extension), d / 'ssa-inputs.json'
    h.cli('compile', root, '--target', target, '--out', optimized, *flags, '--dispatch', 'ssa', '--write-input-lock', optimized_lock)
    optimized_bytes = optimized.read_bytes()
    h.cli('compile', relocated / root.name, '--target', target, '--out', optimized, '--reference-dir', relocated,
          '--dispatch', 'ssa', '--input-lock', optimized_lock)
    assert optimized.read_bytes() == optimized_bytes and h.run(runtime, optimized).stdout == '42\n'
    # Exact identity match plus different content must fail, not select a search-directory winner.
    emit(h, wrong, 'public static class Leaf { public static int Value()=>99; }', 'Leaf')
    rejected(h, 'TR3032', root, target, output, *flags, '--reference-dir', wrong)
    # A correct identity with changed bytes must also fail when it is the only candidate.
    rejected(h, 'TR3311', root, target, output, '--reference-dir', wrong, '--reference-dir', right, '--input-lock', lock)
    # A stale assembly version and a reference-only image cannot satisfy implementation resolution.
    emit(h, wrong, '[assembly:System.Reflection.AssemblyVersion("2.0.0.0")] public static class Leaf{public static int Value()=>42;}', 'Leaf')
    rejected(h, 'TR3031', root, target, output, '--reference-dir', wrong, '--reference-dir', right)
    emit(h, reference, '[assembly:System.Runtime.CompilerServices.ReferenceAssembly] public static class Leaf{public static int Value()=>0;}', 'Leaf')
    rejected(h, 'TR3031', root, target, output, '--reference-dir', reference, '--reference-dir', right)
    rejected(h, 'TR3001', root, target, output, '--reference', reference / 'Leaf.dll', '--reference', middle)
    (corrupt / 'Leaf.dll').write_bytes(b'corrupt')
    rejected(h, 'TR3030', root, target, output, *flags, '--reference-dir', corrupt)
    rejected(h, 'TR3031', root, target, output, '--reference-dir', right)
    middle_bytes = middle.read_bytes()
    r = h.cli('compile', root, '--target', target, '--out', output, '--reference-dir', right, '--diagnostics', middle, expected=None)
    assert r.returncode == 1 and middle.read_bytes() == middle_bytes
    rejected(h, 'TR3311', root, target, output, *flags, '--input-lock', lock, '--dispatch', 'ssa')
    rejected(h, 'TR3300', root, target, output, *flags, '--max-input-bytes', '8')
    rejected(h, 'TR3300', root, target, output, *flags, '--max-total-input-bytes', str(root.stat().st_size + 1))
    rejected(h, 'TR3300', root, target, output, *flags, '--max-input-files', '1')
    # Changes which don't change executable IL are nevertheless input drift.
    mutated = d / 'Mutated.dll'; mutated.write_bytes(root.read_bytes() + b'changed-input')
    rejected(h, 'TR3311', mutated, target, output, *flags, '--input-lock', lock)
    # A rejected replay preserves a previous output, rather than installing unverified code.
    output.write_bytes(b'previous-output')
    r = h.cli('compile', mutated, '--target', target, '--out', output, *flags, '--input-lock', lock, expected=None)
    assert r.returncode == 1 and output.read_bytes() == b'previous-output'
    # Invalid JSON/policy and colliding diagnostics may not destroy the input.
    bad = d / 'bad-lock.json'; bad.write_text(recorded.decode().replace('"schema": 1', '"schema": 99'))
    rejected(h, 'TR3310', root, target, output, *flags, '--input-lock', bad)
    before = root.read_bytes()
    r = h.cli('compile', root, '--target', target, '--out', output, '--diagnostics', root, *flags, expected=None)
    assert r.returncode == 2 and root.read_bytes() == before
    return dict(assemblies=3, metadataCycle=True, relocatableLock=True, quotaAndFailurePaths=True)


def source_lock(h, target, extension):
    d = h.OUT / ('input-source-' + target); d.mkdir(parents=True, exist_ok=True)
    source = d / 'Library.cs'
    source.write_text('using System.Threading.Tasks; public static class Kernel {public static async Task<int> Value(){await Task.Yield();return 42;}}')
    lock, output = d / 'inputs.json', d / ('library.' + extension)
    flags = ['--library', '--bcl', 'portable']
    h.cli('compile', source, '--target', target, '--out', output, *flags, '--write-input-lock', lock)
    baseline = output.read_bytes(); data = json.loads(lock.read_text())
    assert len(data['referencePack']) > 100
    assert any('Transpiler.Bcl,' in a['identity'] for a in data['assemblies'])
    assert any('System.Private.CoreLib,' in a['identity'] for a in data['assemblies'])
    assert any('Microsoft.CodeAnalysis.CSharp,' in a['identity'] for a in data['toolchain'])
    h.cli('compile', source, '--target', target, '--out', output, *flags, '--input-lock', lock)
    assert output.read_bytes() == baseline
    data['referencePack'][0]['sha256'] = '0' * 64
    changed = d / 'changed.json'; changed.write_text(json.dumps(data))
    rejected(h, 'TR3311', source, target, output, *flags, '--input-lock', changed)
    data = json.loads(lock.read_text()); data['toolchain'][0]['sha256'] = '0' * 64; changed.write_text(json.dumps(data))
    rejected(h, 'TR3311', source, target, output, *flags, '--input-lock', changed)
    source.write_text(source.read_text().replace('return 42', 'return 43'))
    rejected(h, 'TR3311', source, target, output, *flags, '--input-lock', lock)
    return dict(sourceReplay=True, referencePackPinned=len(data['referencePack']), toolchainPinned=len(data['toolchain']))


def forwarding(h):
    d = h.OUT / 'input-forwarding'; d.mkdir(parents=True, exist_ok=True)
    contracts = emit(h, d, 'namespace Moved {public static class Number{public static int Value()=>73;}}', 'Contracts')
    root = emit(h, d, 'public static class Program {public static void Main(){System.Console.WriteLine(Moved.Number.Value());}}', 'ForwardApp', [contracts], False)
    destination = emit(h, d, 'namespace Moved {public static class Number{public static int Value()=>73;}}', 'Destination')
    emit(h, d, '[assembly:System.Runtime.CompilerServices.TypeForwardedTo(typeof(Moved.Number))]', 'Contracts', [destination])
    oracle = h.run('dotnet', root).stdout; assert oracle == '73\n'
    for target, extension, runtime in [('js', 'mjs', 'node'), ('py', 'py', sys.executable)]:
        output = d / ('forwarded.' + extension); manifest = d / (target + '.json')
        h.cli('compile', root, '--reference-dir', d, '--target', target, '--out', output, '--manifest', manifest)
        assert h.run(runtime, output).stdout == oracle
        data = json.loads(manifest.read_text()); assert data['forwardings'] and len(data['assemblies']) == 3
    return dict(forwardedUnchangedConsumer=True, targets=2)
