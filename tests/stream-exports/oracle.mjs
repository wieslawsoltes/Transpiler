import {invoke, stream} from './library.mjs';
async function consume(n, options = {}) {
    for await (const value of stream('StreamExports::' + n, [], options)) console.log(value);
}
for (const n of ['Concrete', 'Struct', 'Interface', 'TaskConcrete', 'TaskStruct', 'AsyncValue']) await consume(n);
await consume('TaskErased', {elementType: 'System.Int32'});
await consume('CompletedSource');
await consume('Covariant');
for await (const pair of stream('StreamExports::ValueElements')) {
    for (const n of ['X', 'Y']) console.log(Object.entries(pair.fields).find(([k]) => k.endsWith('::' + n))[1].toString());
}
console.log(invoke('StreamExports::Consumptions'));
console.log(invoke('StreamExports::Disposals'));
