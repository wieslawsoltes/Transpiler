"""End-to-end linking, provenance, cooperative host ABI and lifetime tests. Imported by conformance.py."""
import json
import sys
from pathlib import Path


def graph(h):
    d = h.OUT / 'assembly-graph'
    d.mkdir(parents=True, exist_ok=True)
    sources = {
        'Leaf': 'namespace Shared { public struct Pair { public int X; public Pair(int x) { X=x; } } public class Base { public virtual int Read(int x) => x+1; } public static class Math { public static T Id<T>(T x)=>x; } }',
        'Middle': 'public class Derived : Shared.Base { public override int Read(int x) => base.Read(x)*3; } public static class Api { public static Shared.Pair Make(int x) => Shared.Math.Id(new Shared.Pair(x)); }',
        'App': 'using System; public static class Program { public static void Main() { Shared.Base value = new Derived(); Console.WriteLine(value.Read(7)); var p=Api.Make(42); Console.WriteLine(p.X); Console.WriteLine(Shared.Math.Id(9007199254740993L)); } }'
    }
    for name, source in sources.items():
        path = d / (name + '.cs'); path.write_text(source, encoding='utf-8')
        refs = [] if name == 'Leaf' else ['--reference', d / 'Leaf.dll']
        if name == 'App': refs += ['--reference', d / 'Middle.dll']
        h.cli('emit-pe', path, '--out', d / (name + '.dll'), *([] if name == 'App' else ['--library']), *refs)
    oracle = h.run('dotnet', d / 'App.dll').stdout
    manifests = []
    for target, extension, engine in [('js', 'mjs', 'node'), ('py', 'py', sys.executable)]:
        output, manifest = d / ('app.' + extension), d / (target + '.json')
        h.cli('compile', d / 'App.dll', '--reference', d / 'Middle.dll', '--reference', d / 'Leaf.dll', '--target', target, '--out', output, '--manifest', manifest)
        assert h.run(engine, output).stdout == oracle
        data = json.loads(manifest.read_text())
        assert len(data['assemblies']) == 3
        assert {'Leaf', 'Middle', 'App'} <= {m['assembly'] for m in data['transpiled']}
        # Input order must not affect the linked source.
        repeat = d / ('repeat.' + extension)
        h.cli('compile', d / 'App.dll', '--reference', d / 'Leaf.dll', '--reference', d / 'Middle.dll', '--target', target, '--out', repeat)
        assert output.read_bytes() == repeat.read_bytes()
        missing = d / ('missing.' + extension)
        result = h.cli('compile', d / 'App.dll', '--reference', d / 'Leaf.dll', '--target', target, '--out', missing, expected=None)
        assert result.returncode == 1 and not missing.exists() and 'TR200' in result.stderr
        manifests.append(data['assemblies'])
    conflict = d / 'v2'; conflict.mkdir(exist_ok=True)
    (conflict / 'Leaf.cs').write_text('[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]' + sources['Leaf'])
    h.cli('emit-pe', conflict/'Leaf.cs', '--library', '--out', conflict/'Leaf.dll')
    rejected = h.cli('compile', d/'App.dll', '--reference', conflict/'Leaf.dll', '--reference', d/'Middle.dll', '--target', 'js', '--out', d/'wrong-version.mjs', expected=None)
    assert rejected.returncode == 1 and 'TR3003' in rejected.stderr and not (d/'wrong-version.mjs').exists()
    return {'stdout': oracle, 'assemblyCount': 3, 'versionMismatchRejected': True}


def provenance(h):
    d = h.OUT / 'bcl-provenance'; d.mkdir(parents=True, exist_ok=True)
    manifest = d / 'manifest.json'
    h.cli('compile', h.ROOT / 'tests/bcl/Collections.cs', '--bcl', 'portable', '--target', 'js', '--out', d / 'program.mjs', '--manifest', manifest)
    data = json.loads(manifest.read_text())
    assert data['bcl'] == 'portable-bcl-v1'
    upstream = [m for m in data['transpiled'] if m['assembly'] == 'System.Private.CoreLib']
    assert len(upstream) == 1 and 'Math::BigMul' in upstream[0]['method'] and upstream[0]['instructions'] > 0
    portable = [m for m in data['transpiled'] if m['assembly'] == 'Transpiler.Bcl']
    assert len(portable) >= 10
    assert all(m['instructions'] > 0 for m in portable)
    assert all(len(a['sha256']) == 64 for a in data['assemblies'])
    return {'originalCoreLibMethods': len(upstream), 'portableManagedMethods': len(portable)}


def async_host(h):
    d = h.OUT / 'async-host'; d.mkdir(parents=True, exist_ok=True)
    source = d / 'AsyncLibrary.cs'
    source.write_text('''using System; using System.Threading.Tasks;
public static class Kernel {
 public static async Task<long> Calculate(int n) { long result=9007199254740993L; for(int i=0;i<n;i++){await Task.Yield(); result+=i;}return result; }
 public static async Task<string> Text(){await Task.Yield();return "translated";}
 public static async Task<bool> Boolean(){await Task.Yield();return true;}
 public static async Task Failure(){await Task.Yield();throw new InvalidOperationException("host fault");}
 public static Task Pending() { return new TaskCompletionSource<int>().Task; }
 public static object Make(){return new object();}
}''', encoding='utf-8')
    for target, extension in [('js','mjs'),('py','py')]:
        h.cli('compile', source, '--library', '--bcl', 'portable', '--target', target, '--out', d / ('library.' + extension))
    (d / 'driver.mjs').write_text('''import {invoke,invokeAsync,retain,dereference,release,runtimeInfo} from './library.mjs';
console.log((await invokeAsync('Kernel::Calculate',[5])).toString());
console.log(await invokeAsync('Kernel::Text'));console.log(await invokeAsync('Kernel::Boolean'));
try{await invokeAsync('Kernel::Failure');}catch(e){console.log(e.message.includes('host fault'));}
try{await invokeAsync('Kernel::Pending',[],{maxSteps:3});}catch(e){console.log(e.message.includes('budget'));}
const o=invoke('Kernel::Make');const h=retain(o);console.log(dereference(h)===o);console.log(runtimeInfo().explicitRoots);
console.log(release(h));console.log(release(h));console.log(runtimeInfo().explicitRoots);
try{dereference(h);}catch{console.log('stale rejected');}
''', encoding='utf-8')
    (d / 'driver.py').write_text('''import asyncio
from library import invoke,invoke_async,retain,dereference,release,runtime_info
async def main():
 print(await invoke_async('Kernel::Calculate',[5]))
 print(await invoke_async('Kernel::Text'))
 print(str(await invoke_async('Kernel::Boolean')).lower())
 try: await invoke_async('Kernel::Failure')
 except Exception as e: print(str('host fault' in str(e)).lower())
 try: await invoke_async('Kernel::Pending',max_steps=3)
 except TimeoutError as e: print(str('budget' in str(e)).lower())
 o=invoke('Kernel::Make');h=retain(o)
 print(str(dereference(h) is o).lower());print(runtime_info()['explicitRoots'])
 print(str(release(h)).lower());print(str(release(h)).lower());print(runtime_info()['explicitRoots'])
 try: dereference(h)
 except ValueError: print('stale rejected')
asyncio.run(main())
''', encoding='utf-8')
    expected = '9007199254741003\ntranslated\ntrue\ntrue\ntrue\ntrue\n1\ntrue\nfalse\n0\nstale rejected\n'
    for engine, path in [('node',d/'driver.mjs'),(sys.executable,d/'driver.py')]:
        result=h.run(engine,path)
        assert result.stdout == expected, result.stdout
    return {'stdout': expected}


def boundaries(h):
    d=h.OUT/'portable-rejections';d.mkdir(parents=True,exist_ok=True)
    samples={
        'forced-gc':'System.GC.Collect();',
        'finalizers':'System.GC.WaitForPendingFinalizers();',
        'resurrection':'var w=new System.WeakReference<object>(new object(),true);',
        'thread-pool':'System.Threading.Tasks.Task.Run(()=>42).GetAwaiter().GetResult();',
        # Int32 Delay is now positive coverage; adjacent overloads still need capabilities.
        'delay-timespan':'System.Threading.Tasks.Task.Delay(System.TimeSpan.FromMilliseconds(1)).GetAwaiter().GetResult();',
        'public-timer':'using var timer=new System.Threading.Timer(_=>{},null,1,1);',
        'timed-wait':'System.Threading.Tasks.Task.CompletedTask.WaitAsync(System.TimeSpan.FromMilliseconds(1));',
        'unsupported-continuation':'System.Threading.Tasks.Task.CompletedTask.ContinueWith(t=>42);',
        'reflection':'System.Console.WriteLine(typeof(Program).Name);',
    }
    for name,statement in samples.items():
        p=d/(name+'.cs');p.write_text('public static class Program {public static void Main(){'+statement+'}}')
        for target in ['js','py']:
            output=d/(name+'.'+target)
            output.unlink(missing_ok=True)
            result=h.cli('compile',p,'--bcl','portable','--target',target,'--out',output,expected=None)
            assert result.returncode == 1 and not output.exists() and 'TR' in result.stderr, f'{name}/{target}: exit={result.returncode}\n{result.stdout}\n{result.stderr}'
    return {'rejectedCategories':len(samples),'targetChecks':len(samples)*2}


def register(h):
    import host_stream_batch
    host_stream_batch.register(h)
    import stream_batch
    stream_batch.register(h)
    import block_batch
    block_batch.register(h)
    import awaitable_batch
    awaitable_batch.register(h)
    import runtime_batch
    import composition_batch
    runtime_batch.register(h)
    composition_batch.register(h)
    h.record('linking/three-assembly-graph',lambda:graph(h))
    h.record('bcl/provenance',lambda:provenance(h))
    h.record('host/async-and-roots',lambda:async_host(h))
    h.record('reject/portable-boundaries',lambda:boundaries(h))
