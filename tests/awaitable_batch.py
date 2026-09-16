"""Additional ownership/host-await checks for managed cancellation and ValueTask bindings."""
import sys

def register(h):
    h.record('host/value-task-and-cancellation-cleanup',lambda:run(h))

def run(h):
    d=h.OUT/'awaitable-host';d.mkdir(parents=True,exist_ok=True)
    (d/'Library.cs').write_text('''using System;using System.Threading;using System.Threading.Tasks;
public static class Kernel {
 static CancellationTokenSource Source = new(); static TaskCompletionSource<int> Pending = new();
 public static object GetSource()=>Source;public static object GetPending()=>Pending.Task;
 public static async ValueTask<long> Calculate(){await Task.Yield();return 9007199254740993L;}
 public static ValueTask<int> Immediate()=>ValueTask.FromResult(42);
 public static async ValueTask NoResult(){await Task.Yield();}
 public static async ValueTask<bool> Boolean(){await Task.Yield();return true;}
 public static ValueTask Canceled()=>ValueTask.FromCanceled(new CancellationToken(true));
 public static async ValueTask<string> Text(){await Task.Yield();return "value task host";}
 public static int CleanCompletedWaits(){for(int i=0;i<128;i++){var s=new TaskCompletionSource<int>();var w=s.Task.WaitAsync(Source.Token);s.SetResult(i);if(w.Result!=i)throw new Exception();}return 128;}
 public static int CleanCanceledWaits(){for(int i=0;i<128;i++){using var s=new CancellationTokenSource();var w=Pending.Task.WaitAsync(s.Token);s.Cancel();if(!w.IsCanceled)throw new Exception();}return 128;}
}''')
    for t,e in [('js','mjs'),('py','py')]:h.cli('compile',d/'Library.cs','--library','--bcl','portable','--target',t,'--out',d/('library.'+e))
    (d/'driver.mjs').write_text('''import {invoke,invokeAsync} from './library.mjs';
console.log((await invokeAsync('Kernel::Calculate')).toString());console.log(await invokeAsync('Kernel::Immediate'));
await invokeAsync('Kernel::NoResult');console.log(await invokeAsync('Kernel::Boolean'));console.log(await invokeAsync('Kernel::Text'));
try{await invokeAsync('Kernel::Canceled');}catch(e){console.log(e.name.endsWith('TaskCanceledException'));}
console.log(invoke('Kernel::CleanCompletedWaits'));console.log(invoke('Kernel::CleanCanceledWaits'));
const source=invoke('Kernel::GetSource'),task=invoke('Kernel::GetPending');
const list=Object.entries(source.fields).find(([k])=>k.endsWith('::_callbacks'))?.[1];
if(list && Object.values(list.fields).some(v=>v!==null))throw new Error('completed waits retain cancellation callbacks');
if(Object.entries(task.fields).find(([k])=>k.endsWith('::_observers'))?.[1]!==null)throw new Error('canceled waits retain task observers');
console.log('subscriptions detached');''')
    (d/'driver.py').write_text('''import asyncio
from library import invoke,invoke_async
async def main():
 print(await invoke_async('Kernel::Calculate'));print(await invoke_async('Kernel::Immediate'))
 await invoke_async('Kernel::NoResult');print(str(await invoke_async('Kernel::Boolean')).lower());print(await invoke_async('Kernel::Text'))
 try: await invoke_async('Kernel::Canceled')
 except Exception as e: print(str(e.value.type.endswith('TaskCanceledException')).lower())
 print(invoke('Kernel::CleanCompletedWaits'));print(invoke('Kernel::CleanCanceledWaits'))
 source=invoke('Kernel::GetSource');task=invoke('Kernel::GetPending')
 lists=[v for k,v in source.fields.items() if k.endswith('::_callbacks')]
 assert len(lists)==1 and (lists[0] is None or all(v is None for v in lists[0].fields.values()))
 assert [v for k,v in task.fields.items() if k.endswith('::_observers')]==[None]
 print('subscriptions detached')
asyncio.run(main())''')
    expected='9007199254740993\n42\ntrue\nvalue task host\ntrue\n128\n128\nsubscriptions detached\n'
    for runtime,driver in [('node',d/'driver.mjs'),(sys.executable,d/'driver.py')]:
        result=h.run(runtime,driver).stdout
        assert result==expected,result
    return dict(completedWaits=128,canceledWaits=128,valueTaskHostABI=True)
