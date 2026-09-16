"""Source-backed ValueTask host ABI, translated algorithm provenance, and explicit remaining boundaries."""
import json
import sys


def register(h):
    h.record('host/value-task-source-protocol', lambda: run(h))


def run(h):
    d = h.OUT / 'source-host'; d.mkdir(parents=True, exist_ok=True)
    (d/'Library.cs').write_text('''using System;using System.Threading.Tasks;using System.Threading.Tasks.Sources;
public sealed class Source : IValueTaskSource<int> {
 public ManualResetValueTaskSourceCore<int> Core; public int Reads;
 public ValueTaskSourceStatus GetStatus(short token)=>Core.GetStatus(token);
 public int GetResult(short token){Reads++;return Core.GetResult(token);}
 public void OnCompleted(Action<object> c,object state,short token,ValueTaskSourceOnCompletedFlags flags)=>Core.OnCompleted(c,state,token,flags);
}
public static class Kernel {
 static Source S=new();
 public static ValueTask<int> Pending()=>new(S,S.Core.Version);
 public static object GetSource()=>S;
 public static void Complete(int n)=>S.Core.SetResult(n);
 public static void Reset()=>S.Core.Reset();
 public static int Reads()=>S.Reads;
 public static Task<int> Adapt()=>Pending().AsTask();
}''', encoding='utf-8')
    counts = []
    for target, ext in [('js','mjs'),('py','py')]:
        manifest = d/(target+'.json')
        h.cli('compile', d/'Library.cs', '--library', '--bcl', 'portable', '--target', target,
              '--out', d/('library.'+ext), '--manifest', manifest, '--dispatch', 'block')
        data = json.loads(manifest.read_text())
        translated = [m for m in data['transpiled'] if m['assembly']=='Transpiler.Bcl']
        for name in ('ManualResetValueTaskSourceCore', 'ValueTaskSourceBridge'):
            assert any(name in m['method'] and m['instructions'] > 0 for m in translated), name
        counts.append(len(translated))
    (d/'driver.mjs').write_text('''import {invoke,invokeAsync} from './library.mjs';
let sent=false;
const value=await invokeAsync('Kernel::Pending',[],{yieldHost:async()=>{if(!sent){sent=true;invoke('Kernel::Complete',[42]);}}});
console.log(value);console.log(invoke('Kernel::Reads'));
invoke('Kernel::Reset');sent=false;
console.log(await invokeAsync('Kernel::Adapt',[],{yieldHost:async()=>{if(!sent){sent=true;invoke('Kernel::Complete',[71]);}}}));
console.log(invoke('Kernel::Reads'));
const source=invoke('Kernel::GetSource');const core=Object.entries(source.fields).find(([k])=>k.endsWith('::Core'))[1];
for(const [k,v] of Object.entries(core.fields))if((k.endsWith('::_continuation')||k.endsWith('::_state'))&&v!==null)throw new Error('source retained callback state');
console.log('callback state released');
''',encoding='utf-8')
    (d/'driver.py').write_text('''import asyncio
from library import invoke,invoke_async
async def complete(n):
 await asyncio.sleep(0)
 invoke('Kernel::Complete',[n])
async def main():
 writer=asyncio.create_task(complete(42))
 print(await invoke_async('Kernel::Pending'));await writer;print(invoke('Kernel::Reads'))
 invoke('Kernel::Reset');writer=asyncio.create_task(complete(71))
 print(await invoke_async('Kernel::Adapt'));await writer;print(invoke('Kernel::Reads'))
 core=next(v for k,v in invoke('Kernel::GetSource').fields.items() if k.endswith('::Core'))
 assert all(v is None for k,v in core.fields.items() if k.endswith('::_continuation') or k.endswith('::_state'))
 print('callback state released')
asyncio.run(main())
''',encoding='utf-8')
    expected='42\n1\n71\n2\ncallback state released\n'
    for host,driver in [('node',d/'driver.mjs'),(sys.executable,d/'driver.py')]:
        result=h.run(host,driver).stdout
        assert result==expected,result
    for name,statement in {
        'remote-stack':'System.Runtime.ExceptionServices.ExceptionDispatchInfo.SetRemoteStackTrace(new System.Exception(),"trace");',
        'execution-context':'System.Threading.ExecutionContext.Capture();'
    }.items():
        source=d/(name+'.cs');source.write_text('public static class Program{public static void Main(){'+statement+'}}')
        for target in ('js','py'):
            output=d/(name+'.'+target);output.unlink(missing_ok=True)
            r=h.cli('compile',source,'--bcl','portable','--target',target,'--out',output,expected=None)
            assert r.returncode==1 and 'TR2002' in r.stderr and not output.exists(),r.stderr
    return dict(translatedManagedMethodsPerTarget=counts,sourceConsumptions=2,callbackStateReleased=True,
                unsupportedContextAndRemoteStackRejected=True)
