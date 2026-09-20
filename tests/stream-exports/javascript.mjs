import assert from 'node:assert/strict';
import {invoke, invokeAsync, stream, streamInfo, runtimeInfo, StreamCleanupPendingError} from './library.mjs';
const name = n => 'StreamExports::' + n;
const call = (n, args = []) => invoke(name(n), args);
const fast = {maxSteps: 64, cleanupSteps: 64, yieldHost: async () => {}};
const values = async s => { const result = []; for await (const value of s) result.push(value); return result; };
const delay = () => new Promise(resolve => setTimeout(resolve, 0));
const cases = [];
function clean() {
    const info = runtimeInfo();
    for (const key of ['activeStreams', 'activeStreamFactories', 'activeTimers', 'hostWaiters']) assert.equal(info[key], 0, key);
}
async function test(n, action) { clean(); call('Reset'); await action(); clean(); cases.push(n); }
async function until(predicate) {
    for (let i = 0; i < 200; ++i) { if (predicate()) return; await delay(); }
    throw new Error('Factory never reached the expected phase');
}
const int = 'System.Int32', text = 'System.String';
for (const n of ['Direct', 'Concrete', 'Inherited', 'Interface', 'Struct', 'TaskConcrete', 'TaskStruct', 'ValueConcrete', 'ValueStruct', 'TaskInterface', 'AsyncValue']) {
    await test('shape/' + n, async () => {
        assert.deepEqual(streamInfo(name(n)).elements, [int]);
        assert.deepEqual(await values(stream(name(n), [], fast)), [10, 11]);
        assert.equal(call('Factories'), 1); assert.equal(call('Acquisitions'), 1); assert.equal(call('Disposals'), 1);
    });
}
await test('metadata-no-acquisition', async () => {
    const info = streamInfo(name('Concrete'));
    assert.equal(info.policy, 'managed-stream-v2'); assert.equal(info.kind, 'value');
    assert.equal(streamInfo(name('TaskConcrete')).kind, 'task');
    assert.equal(streamInfo(name('ValueConcrete')).kind, 'value-task');
    assert.throws(() => info.elements.push('System.Double'), TypeError);
    assert.equal(call('Factories'), 0);
    const unused = stream(name('TaskConcrete')); await unused.return(); assert.equal(call('Factories'), 0);
});
for (const [n, args] of [['Erased', [0]], ['Erased', [1]], ['TaskErased', []], ['ValueErased', []]]) {
    await test('erased/' + n + args.join(), async () => {
        assert.equal(streamInfo(name(n)).requiresElement, true);
        assert.throws(() => stream(name(n), args), /elementType/);
        assert.equal(call('Factories'), 0);
        assert.deepEqual(await values(stream(name(n), args, {...fast, elementType: int})), [10, 11]);
    });
}
for (const n of ['Multiple', 'TaskMultiple']) {
    await test('ambiguous/' + n, async () => {
        assert.deepEqual(streamInfo(name(n)).elements, [int, text]);
        assert.throws(() => stream(name(n)), /elementType/);
        assert.equal(call('Factories'), 0);
        assert.deepEqual(await values(stream(name(n), [], {...fast, elementType: int})), [10, 11]);
        assert.deepEqual(await values(stream(name(n), [], {...fast, elementType: text})), ['alpha', 'beta']);
    });
}
await test('rejections-before-user-code', async () => {
    for (const n of ['NonStream', 'NestedUnsupported']) assert.throws(() => stream(name(n)), /no linked stream/);
    assert.throws(() => stream(name('Concrete'), [], {elementType: 'System.Double'}), /not linked/);
    assert.throws(() => stream(name('Concrete'), [], {elementType: null}), /elementType/);
    assert.throws(() => stream(name('Concrete'), [1]), /argument count/);
    assert.throws(() => stream(name('Overloaded')), /unambiguous/);
    assert.equal(call('Factories'), 0);
});
await test('overload-exact-signature', async () => {
    assert.deepEqual(await values(stream(name('Overloaded(System.Int32)'), [20], fast)), [20, 21]);
});
for (const [n, args, error] of [
    ['ThrowingFactory', [], 'InvalidOperationException'], ['FaultedFactory', [], 'InvalidOperationException'],
    ['CanceledFactory', [], 'TaskCanceledException'], ['NullDirect', [], 'ArgumentNullException'],
    ['NullTask', [], 'ArgumentNullException'], ['NullResult', [], 'ArgumentNullException'],
    ['DefaultValue', [], 'ArgumentNullException'], ['BadAcquisition', [], 'InvalidOperationException'],
    ['NullEnumerator', [], 'InvalidOperationException'], ['Erased', [2], 'InvalidCastException'],
    ['Erased', [3], 'ArgumentNullException']]) {
    await test('failure/' + n + args.join(), async () => {
        const s = stream(name(n), args, {...fast, ...(n === 'Erased' ? {elementType: int} : {})});
        await assert.rejects(s.next(), e => e.name.endsWith(error));
        assert.equal(s.closed, true); await s.return(); assert.equal(call('Factories'), 1);
    });
}
await test('checked-erased-interface-cast', async () => {
    const s = stream(name('Erased'), [0], {...fast, elementType: text});
    await assert.rejects(s.next(), e => e.name.endsWith('InvalidCastException'));
    assert.equal(call('Acquisitions'), 0);
});
await test('source-completed-consumed-once', async () => {
    assert.deepEqual(await values(stream(name('CompletedSource'), [], fast)), [10, 11]);
    assert.equal(call('Consumptions'), 1);
});
await test('factory-cleanup-timeout-and-retry', async () => {
    const s = stream(name('SourceFactory'), [], {...fast, maxSteps: 2, cleanupSteps: 2});
    for (const operation of [() => s.next(), () => s.return()]) {
        await assert.rejects(operation(), e => e instanceof StreamCleanupPendingError && e.phase === 'factory');
        assert.equal(s.pending, 'factory'); assert.equal(s.closed, false);
        assert.equal(runtimeInfo().activeStreamFactories, 1); assert.equal(call('Consumptions'), 0);
    }
    call('CompleteSource'); await s.return();
    assert.equal(call('Factories'), 1); assert.equal(call('Consumptions'), 1);
    assert.equal(call('AcquiredCanceled'), true); assert.equal(call('Moves'), 0); assert.equal(call('Disposals'), 1);
});
await test('pending-factory-fault-retires-on-close', async () => {
    const s = stream(name('PendingFactory'), [], {...fast, maxSteps: 1, cleanupSteps: 1});
    await assert.rejects(s.next(), e => e.phase === 'factory');
    call('FailFactory'); await assert.rejects(s.return(), /factory-failure/);
    assert.equal(s.closed, true); assert.equal(call('Acquisitions'), 0);
});
await test('close-while-acquiring-without-move', async () => {
    const s = stream(name('DelayedFactory'));
    const move = s.next(); const observed = move.catch(e => e);
    await until(() => s.pending === 'factory' && runtimeInfo().hostWaiters === 1);
    await s.return(); await observed;
    assert.equal(call('Factories'), 1); assert.equal(call('Acquisitions'), 1);
    assert.equal(call('AcquiredCanceled'), true); assert.equal(call('Moves'), 0); assert.equal(call('Disposals'), 1);
});
await test('abort-during-factory-drains-ownership', async () => {
    const controller = new AbortController(); const s = stream(name('DelayedFactory'), [], {signal: controller.signal});
    const move = s.next(); const observed = assert.rejects(move, /abort-factory/);
    await until(() => s.pending === 'factory' && runtimeInfo().hostWaiters === 1);
    controller.abort(new Error('abort-factory')); await observed;
    assert.equal(call('AcquiredCanceled'), true); assert.equal(call('Moves'), 0); assert.equal(call('Disposals'), 1);
});
await test('pre-canceled-factory-not-invoked', async () => {
    const c = new AbortController(); c.abort(new Error('before-open'));
    const s = stream(name('TaskConcrete'), [], {signal: c.signal});
    await assert.rejects(s.next(), /before-open/); assert.equal(call('Factories'), 0);
});
await test('acquisition-and-disposal-delays', async () => {
    const s = stream(name('DelayedDispose'));
    assert.deepEqual(await s.next(), {done: false, value: 10});
    await s.return(); assert.equal(call('Disposals'), 1);
});
await test('disposal-fault-retires', async () => {
    const s = stream(name('FaultingDispose'), [], fast);
    await s.next(); await assert.rejects(s.return(), /dispose-failure/);
    assert.equal(s.closed, true); assert.equal(call('Disposals'), 1); await s.return();
});
await test('source-completed-by-custom-yield', async () => {
    let supplied = false;
    const s = stream(name('SourceFactory'), [], {...fast, yieldHost: async () => {
        if (!supplied) { supplied = true; call('CompleteSource'); }
    }});
    assert.deepEqual(await values(s), [10, 11]); assert.equal(call('Consumptions'), 1);
});
await test('overlapping-factory-next-rejected', async () => {
    const s = stream(name('DelayedFactory')); const first = s.next();
    await assert.rejects(s.next(), /Overlapping/);
    assert.equal((await first).value, 10); await s.return(); assert.equal(call('Factories'), 1);
});
await test('two-independent-factories', async () => {
    const a = stream(name('DelayedFactory')), b = stream(name('DelayedFactory'));
    assert.deepEqual(await Promise.all([values(a), values(b)]), [[10, 11], [10, 11]]);
    assert.equal(call('Factories'), 2); assert.equal(call('Disposals'), 2);
});
await test('structured-elements-preserve-int64', async () => {
    const result = await values(stream(name('ValueElements'), [], fast));
    const field = (v, n) => Object.entries(v.fields).find(([k]) => k.endsWith('::' + n))[1];
    assert.equal(field(result[0], 'X'), 9007199254740993n);
    assert.equal(field(result[1], 'X'), 9007199254740995n);
    assert.equal(field(result[0], 'Y'), 1);
});
await test('retained-elements-and-factory-stress', async () => {
    for (let n = 0; n < 40; ++n) {
        const s = stream(name('TaskConcrete'), [], fast); assert.equal((await s.next()).value, 10); await s.return();
    }
    assert.equal(call('Factories'), 40); assert.equal(call('Disposals'), 40);
});
console.log(JSON.stringify({cases: cases.length, names: cases, info: runtimeInfo()}));
