"""Task composition retention checks beyond stdout-only differential tests."""
import json
import sys

def register(h):
    h.record('host/whenany-loser-cleanup', lambda: cleanup(h))

def cleanup(h):
    d = h.OUT / 'whenany-cleanup'; d.mkdir(parents=True, exist_ok=True)
    source = d / 'Races.cs'
    source.write_text("""using System; using System.Threading.Tasks;
public static class Kernel {
 static readonly TaskCompletionSource<int> Pending = new TaskCompletionSource<int>();
 public static object Loser() => Pending.Task;
 public static int Exercise() {
  for(int i=0;i<128;i++) {
   var fast=new TaskCompletionSource<int>();var race=Task.WhenAny(new[]{Pending.Task,fast.Task});
   fast.SetResult(i);
   if(!object.ReferenceEquals(race.Result,fast.Task))throw new Exception("Wrong winner");
  }
  return 128;
 }
}""")
    for target, ext in [('js','mjs'),('py','py')]:
        manifest = d/(target+'.json')
        h.cli('compile', source, '--library', '--bcl', 'portable', '--target', target,
              '--out', d/('library.'+ext), '--manifest', manifest)
        emitted = json.loads(manifest.read_text())['transpiled']
        assert any(m['assembly']=='Transpiler.Bcl' and 'WhenAny' in m['method'] for m in emitted)
    (d/'driver.mjs').write_text("""import {invoke} from './library.mjs';
console.log(invoke('Kernel::Exercise'));
const entries=Object.entries(invoke('Kernel::Loser').fields).filter(([key])=>key.endsWith('::_observers'));
if(entries.length!==1||entries[0][1]!==null)throw new Error('A pending loser retains completed races');
console.log('loser observers detached');
""")
    (d/'driver.py').write_text("""from library import invoke
print(invoke('Kernel::Exercise'))
entries=[v for k,v in invoke('Kernel::Loser').fields.items() if k.endswith('::_observers')]
assert entries == [None], 'A pending loser retains completed races'
print('loser observers detached')
""")
    expected='128\nloser observers detached\n'
    for runtime, path in [('node',d/'driver.mjs'),(sys.executable,d/'driver.py')]:
        actual=h.run(runtime,path).stdout
        assert actual==expected, actual
    return {'completedRaces':128,'pendingLoserHasNoObservers':True,'managedImplementationOriginVerified':True}
