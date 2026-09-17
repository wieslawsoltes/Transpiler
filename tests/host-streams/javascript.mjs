import assert from 'node:assert/strict';
import {stream, invoke, runtimeInfo, StreamCleanupPendingError} from './library.mjs';
const i = (name, args = []) => invoke('Kernel::' + name, args);
const fast = {yieldHost: async () => {}, maxSteps: 32, cleanupSteps: 32};
const collect = async s => {const values=[];for await(const value of s)values.push(value);return values;};
let cases = 0;
async function test(name, action) { await action(); assert.equal(runtimeInfo().activeStreams,0,name); cases++; }
await test('lazy construction and unused close', async()=>{
 const n=i('Factories');const s=stream('Kernel::Numbers',[3],fast);assert.equal(i('Factories'),n);
 await s.return();assert.equal(i('Factories'),n);assert.equal((await s.next()).done,true);assert(s.closed);
});
await test('normal exhaustion and repeated close', async()=>{
 const before=i('Closed');const s=stream('Kernel::Numbers',[3],fast);
 assert.deepEqual(await collect(s),[9007199254740993n,9007199254740994n,9007199254740995n]);
 assert.equal(i('Closed'),before+1);await s.return();assert.equal(i('Closed'),before+1);assert(s.closed);
});
await test('early break',async()=>{
 const before=i('Closed');const s=stream('Kernel::Numbers',[3],fast);for await(const v of s){assert.equal(v,9007199254740993n);break;}
 assert.equal(i('Closed'),before+1);assert(s.closed);
});
await test('body exception closes',async()=>{
 const error=new Error('body'),before=i('Closed');
 await assert.rejects(async()=>{for await(const v of stream('Kernel::Numbers',[3],fast)){throw error;}},e=>e===error);
 assert.equal(i('Closed'),before+1);
});
await test('primitive and struct marshalling',async()=>{
 assert.deepEqual(await collect(stream('Kernel::Text',[],fast)),['A🚂B',null]);
 assert.deepEqual(await collect(stream('Kernel::Flags',[],fast)),[true,false]);
 const f=await collect(stream('Kernel::Floats',[],fast));assert(Number.isNaN(f[0]));assert(Object.is(f[1],-0));assert.equal(f[2],Infinity);
 const s=stream('Kernel::Items',[],fast),a=(await s.next()).value;const key=Object.keys(a.fields).find(k=>k.endsWith('::X'));a.fields[key]=99;
 assert.equal((await s.next()).value.fields[key],7);await s.return();
});
await test('independent simultaneous enumerators',async()=>{
 const a=stream('Kernel::Numbers',[2],fast),b=stream('Kernel::Numbers',[2],fast);
 assert.deepEqual(await Promise.all([a.next(),b.next()]),[{done:false,value:9007199254740993n},{done:false,value:9007199254740993n}]);
 assert.equal(runtimeInfo().activeStreams,2);await Promise.all([a.return(),b.return()]);
});
await test('single source consumption and field retirement',async()=>{
 const s=stream('Kernel::Probe',[0],fast);assert.equal((await s.next()).value,1);const cursor=s.cursor;await s.return();
 assert.equal(i('Moves'),1);assert.equal(i('Reads'),1);assert.equal(i('Disposes'),1);assert.equal(i('DisposeReads'),1);
 for(const [key,value] of Object.entries(cursor.fields))if(key.endsWith('::_enumerator')||key.endsWith('::_cancellation'))assert.equal(value,null);
 assert.equal(s.cursor,null);assert.equal(s.yieldHost,null);
});
for(const [mode,part] of [[3,'current fault'],[4,'move call fault'],[5,'move result fault']])
 await test(part,async()=>{const s=stream('Kernel::Probe',[mode],fast);await assert.rejects(s.next(),e=>e.message.includes(part));assert.equal(i('Disposes'),1);assert(s.closed);});
for(const [mode,part] of [[6,'dispose call fault'],[7,'dispose result fault']])
 await test(part,async()=>{const s=stream('Kernel::Probe',[mode],fast);await s.next();await assert.rejects(s.return(),e=>e.message.includes(part));assert(s.closed);await s.return();assert.equal(i('Disposes'),1);});
await test('acquisition failures',async()=>{
 for(const name of ['Null','FactoryFault']){const s=stream('Kernel::'+name,[],fast);await assert.rejects(s.next());assert(s.closed);}
 for(const mode of [10,11]){const s=stream('Kernel::Probe',[mode],fast);await assert.rejects(s.next());assert(s.closed);assert.equal(i('Disposes'),0);}
});
await test('external source completion',async()=>{
 let sent=false;const s=stream('Kernel::Probe',[1],{...fast,yieldHost:async()=>{if(!sent){sent=true;i('CompleteMove',[true]);}}});
 assert.equal((await s.next()).value,1);await s.return();assert.equal(i('Moves'),1);assert.equal(i('Reads'),1);
});
await test('abort while moving and detach signal',async()=>{
 const controller=new AbortController(),reason=new Error('abort reason');let added=0,removed=0;
 const signal={get aborted(){return controller.signal.aborted},get reason(){return controller.signal.reason},addEventListener(...a){added++;controller.signal.addEventListener(...a)},removeEventListener(...a){removed++;controller.signal.removeEventListener(...a)}};
 const s=stream('Kernel::Probe',[2],{...fast,signal,yieldHost:async()=>{controller.abort(reason);}});
 await assert.rejects(s.next(),e=>e===reason);assert.equal(i('Canceled'),1);assert.equal(i('Reads'),1);assert.equal(i('Disposes'),1);assert.equal(added,removed);assert(s.closed);
});
await test('pre-aborted stream never opens',async()=>{
 const c=new AbortController();c.abort();const n=i('Factories');const s=stream('Kernel::Numbers',[3],{...fast,signal:c.signal});await assert.rejects(s.next());assert.equal(n,i('Factories'));assert(s.closed);
});
await test('close during outstanding move',async()=>{
 let entered;const reached=new Promise(r=>entered=r);const s=stream('Kernel::Probe',[2],{...fast,yieldHost:async()=>{entered();await new Promise(r=>setTimeout(r,0));}});
 const move=s.next();await reached;await assert.rejects(s.next(),/Overlapping/);const closing=s.return();await assert.rejects(move);await closing;assert(s.closed);assert.equal(i('Reads'),1);assert.equal(i('Disposes'),1);
});
await test('timeout preserves move and retries safely',async()=>{
 const s=stream('Kernel::Probe',[1],{...fast,maxSteps:2,cleanupSteps:2});await assert.rejects(s.next(),e=>e instanceof StreamCleanupPendingError&&e.phase==='move');
 assert.equal(s.pending,'move');assert(!s.closed);assert.equal(i('Reads'),0);assert.equal(i('Disposes'),0);assert.equal(runtimeInfo().activeStreams,1);
 await assert.rejects(s.next(),/cleanup/);i('CompleteMove',[false]);await s.return();assert.equal(i('Moves'),1);assert.equal(i('Reads'),1);assert.equal(i('Disposes'),1);
});
await test('timeout preserves disposal and retries once',async()=>{
 const s=stream('Kernel::Probe',[8],{...fast,maxSteps:2,cleanupSteps:2});await s.next();await assert.rejects(s.return(),e=>e instanceof StreamCleanupPendingError&&e.phase==='dispose');
 assert.equal(s.pending,'dispose');assert.equal(i('Disposes'),1);assert.equal(i('DisposeReads'),0);i('CompleteDispose');await s.return();assert(s.closed);assert.equal(i('Disposes'),1);assert.equal(i('DisposeReads'),1);
});
await test('throw closes without injecting host exception',async()=>{
 const s=stream('Kernel::Probe',[0],fast);await s.next();const error=new Error('host');await assert.rejects(s.throw(error),e=>e===error);assert.equal(i('Disposes'),1);
});
await test('JavaScript body exception has native precedence',async()=>{
 const error=new Error('body');await assert.rejects(async()=>{for await(const value of stream('Kernel::Probe',[7],fast)){throw error;}},e=>e===error);assert.equal(i('Disposes'),1);
});
await test('validation before invoking code',async()=>{
 const n=i('Factories');for(const options of [{maxSteps:0},{cleanupSteps:true},{yieldHost:0},{signal:{}}])assert.throws(()=>stream('Kernel::Numbers',[1],options));
 assert.throws(()=>stream('Kernel::Closed',[],fast));assert.throws(()=>stream('missing',[],fast));assert.throws(()=>stream('Kernel::Numbers',[],fast));assert.equal(i('Factories'),n);
});
console.log(JSON.stringify({host:'javascript',cases,activeStreams:runtimeInfo().activeStreams}));
