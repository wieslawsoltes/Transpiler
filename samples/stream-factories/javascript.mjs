import {invoke, stream, streamInfo, runtimeInfo} from './kernel.mjs';

// A Task<Counter> result is acquired by the stream adapter; do not invoke the factory twice.
for await (const value of stream('StreamFactories::Delayed', [3])) {
    console.log(value.toString());
    if (value === 9007199254740994n) break; // return() awaits managed disposal.
}

const description = streamInfo('StreamFactories::Erased');
if (!description.elements.includes('System.Int64')) throw new Error('Int64 stream was not linked');
for await (const value of stream('StreamFactories::Erased', [1], {elementType: 'System.Int64'}))
    console.log(value.toString());

console.log(invoke('StreamFactories::Disposed')); // 2
const info = runtimeInfo();
console.log(info.activeStreams, info.activeStreamFactories, info.activeTimers); // 0 0 0
