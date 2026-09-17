import { stream, runtimeInfo } from './kernel.mjs';

let sum = 0n;
for await (const value of stream('StreamKernel::Squares', [20])) {
    sum += value;
    if (value === 16n) break;
}
console.log(sum.toString());
console.log(runtimeInfo().activeStreams);
