"""Original CoreLib adoption, local initialization proofs, and managed/host heap boundary gates."""
import json
import sys

def register(h):
    h.record('compiler/definite-assignment', lambda: compiler_checks(h))
    h.record('bcl/original-catalog', lambda: original_catalog(h))
    h.record('host/liveness-barrier', lambda: liveness_barrier(h))
    for debug in (False, True):
        h.record('runtime/logical-heap/' + ('debug' if debug else 'release'),
                 lambda debug=debug: logical_heap(h, debug))


def compiler_checks(h):
    output = h.run('dotnet', h.ROOT/'tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll').stdout
    assert output == 'Definite assignment: 11 passed\n', output
    return {'assertedShapes': 11}


def original_catalog(h):
    d = h.OUT/'original-catalog'; d.mkdir(parents=True, exist_ok=True)
    counts = []
    for target, extension in [('js', 'mjs'), ('py', 'py')]:
        manifest = d/(target+'.json')
        output = d/('program.'+extension)
        h.cli('compile', h.ROOT/'tests/bcl/OriginalMath.cs', '--bcl', 'portable',
              '--target', target, '--out', output, '--manifest', manifest)
        data = json.loads(manifest.read_text())
        original = [m for m in data['transpiled'] if m['assembly'] == 'System.Private.CoreLib']
        assert len(original) == 21, original
        assert all(m['instructions'] > 0 for m in original)
        assert not any('System.Math::' in m['method'] for m in data['intrinsics'])
        assert 'Copyright (c) .NET Foundation' in output.read_text()
        counts.append(len(original))
    return {'originalManagedMethodsPerTarget': counts}


def logical_heap(h, debug):
    import shutil
    d = h.OUT/('logical-heap-debug' if debug else 'logical-heap-release'); d.mkdir(parents=True, exist_ok=True)
    dependency = h.ROOT/'src/Transpiler.Runtime.Managed/bin/Release/net10.0/Transpiler.Runtime.Managed.dll'
    shutil.copy2(dependency, d/dependency.name)
    assembly = d/'LogicalHeap.dll'
    h.cli('emit-pe', h.ROOT/'tests/runtime/LogicalHeap.cs', '--reference', dependency,
          '--out', assembly, *(['--debug'] if debug else []))
    oracle = h.run('dotnet', assembly).stdout
    expected = 'generation checked\nreleased root checked\nownership checked\nquota checked\nbounds checked\n963\nlogical heap verified\n'
    assert oracle == expected, oracle
    for target, extension, host in [('js', 'mjs', 'node'), ('py', 'py', sys.executable)]:
        output = d/('heap.'+extension); manifest = d/(target+'.json')
        h.cli('compile', assembly, '--reference', dependency, '--bcl', 'portable', '--target', target,
              '--out', output, '--manifest', manifest)
        assert h.run(host, output).stdout == oracle
        translated = [m for m in json.loads(manifest.read_text())['transpiled'] if m['assembly'] == 'Transpiler.Runtime.Managed']
        assert len(translated) >= 15 and any('::Collect(' in m['method'] for m in translated)
        repeat = d/('repeat.'+extension)
        h.cli('compile', assembly, '--reference', dependency, '--bcl', 'portable', '--target', target, '--out', repeat)
        assert output.read_bytes() == repeat.read_bytes()
    return {'stdout': oracle, 'graphOracleRounds': 20, 'logicalAllocations': 963, 'ordinaryObjectsUseHostGC': True}


def liveness_barrier(h):
    d = h.OUT/'liveness-barrier'; d.mkdir(parents=True, exist_ok=True)
    source = d/'Liveness.cs'
    source.write_text('public static class Kernel { public static object Make() => new object(); public static void Hold(object value) => System.GC.KeepAlive(value); }')
    h.cli('compile', source, '--library', '--target', 'js', '--out', d/'library.mjs')
    driver = d/'driver.mjs'
    driver.write_text("""import {invoke,runtimeInfo} from './library.mjs';
const NativeWeakRef=globalThis.WeakRef;let constructions=0,dereferences=0;
globalThis.WeakRef=class extends NativeWeakRef {
 constructor(value){super(value);constructions++;}
 deref(){dereferences++;return super.deref();}
};
try {
 const value=invoke('Kernel::Make');invoke('Kernel::Hold',[value]);
 if(constructions!==1||dereferences!==1)throw new Error('Missing specified liveness barrier');
 invoke('Kernel::Hold',[null]);
 globalThis.WeakRef=undefined;let rejected=false;
 try{invoke('Kernel::Hold',[value]);}catch(e){rejected=e.message.includes('WeakRef');}
 if(!rejected||runtimeInfo().weakReferences)throw new Error('Missing capability failure');
 console.log('host liveness verified');
}finally{globalThis.WeakRef=NativeWeakRef;}
""", encoding='utf-8')
    assert h.run('node',driver).stdout == 'host liveness verified\n'
    return {'specifiedKeptAliveBarrier':True,'unavailableHostCapabilityRejected':True}
