import assert from 'node:assert/strict';
import {invoke, invokeAsync, runtimeInfo, setClock} from './library.mjs';
const call = (name, args = []) => invoke('TimeKernel::' + name, args);
const wait = (name, args = []) => invokeAsync('TimeKernel::' + name, args);
const pump = () => wait('Pump');
let cases = 0;
const mark = name => { ++cases; console.log('PASS time/' + name); };
const clean = () => {
    const info = runtimeInfo();
    for (const key of ['activeTimers', 'readyTimers', 'hostWaiters', 'activeStreams']) assert.equal(info[key], 0, key);
};
const field = (object, name) => Object.entries(object.fields).find(([key]) => key.endsWith('::' + name))?.[1];
const detached = () => {
    assert.equal(field(call('GetInput'), '_observers'), null);
    const list = field(call('GetSource'), '_callbacks');
    assert.ok(list === null || field(list, 'Head') === null);
};
class ManualClock {
    time = 0; events = [];
    now() { return this.time; }
    schedule(ms, callback) { const event = {due: this.time + ms, callback, active: true}; this.events.push(event); return event; }
    cancel(event) { event.active = false; }
    advance(ms) {
        this.time += ms;
        for (const event of [...this.events]) if (event.active && event.due <= this.time) { event.active = false; event.callback(); }
    }
}
const clock = new ManualClock(); setClock(clock);
for (const [mode, expected] of [['success', 73], ['timeout', -2], ['cancel', -3], ['fault', -5]]) {
    call('Reset'); call('StartWait', [100000n]);
    assert.equal(runtimeInfo().activeTimers, 1);
    if (mode === 'success') call('CompleteInput');
    else if (mode === 'timeout') clock.advance(10);
    else if (mode === 'cancel') call('Cancel');
    else call('FailInput');
    assert.equal(await wait('AwaitWait'), expected);
    if (mode === 'timeout' || mode === 'cancel') assert.equal(call('InputCompleted'), false);
    detached(); clean(); mark('wait-' + mode);
}
call('Reset'); call('StartWait', [109999n]);
assert.equal(clock.events.at(-1).due - clock.time, 10);
clock.advance(10); assert.equal(await wait('AwaitWait'), -2); clean(); mark('duration-truncation');

call('Reset'); const longDelay = wait('Delay', [4294967294n]);
assert.equal(clock.events.at(-1).due - clock.time, 4294967294);
clock.advance(2147483647); assert.equal(runtimeInfo().readyTimers, 0);
clock.advance(2147483647); assert.equal(await longDelay, 42); clean(); mark('uint-range-delay');

call('Reset'); call('StartTimer', [10n, 10n]); clock.advance(10);
assert.equal(call('Count'), 0); await pump(); assert.equal(call('Count'), 1);
clock.advance(50); await pump(); assert.equal(call('Count'), 2);
call('ChangeTimer', [-1n, 0n]); clock.advance(100); await pump(); assert.equal(call('Count'), 2);
call('DisposeTimer'); clean(); mark('serialized-periodic-timer');

call('Reset'); call('CallbackChange'); clock.advance(10); await pump();
assert.equal(call('Count'), 1); clock.advance(10); await pump(); assert.equal(call('Count'), 1);
clock.advance(15); await pump(); assert.equal(call('Count'), 2);
clock.advance(100); await pump(); assert.equal(call('Count'), 2);
call('DisposeTimer'); clean(); mark('reentrant-change-wins');

call('Reset'); call('CallbackDisposal'); clock.advance(10); await pump();
assert.equal(call('DisposalPendingInsideCallback'), true); assert.equal(await wait('ObserveDisposal'), true);
assert.equal(call('Count'), 1); clock.advance(100); await pump(); assert.equal(call('Count'), 1);
clean(); mark('dispose-async-waits-for-callback-exit');

call('Reset'); call('CallbackOnlyConstructor'); clock.advance(5); await pump();
assert.equal(call('Count'), 1); assert.equal(await wait('DrainTimer'), true); clean(); mark('callback-only-state');

call('Reset'); call('StartTimer', [0n, 0n]); assert.equal(call('Count'), 0);
clock.advance(0); await pump(); assert.equal(call('Count'), 1);
clock.advance(100); await pump(); assert.equal(call('Count'), 1);
call('DisposeTimer'); clean(); mark('zero-period-single-shot');

call('Reset'); call('StartPeriodic', [10n]);
clock.advance(100); await pump(); call('BeginTick'); assert.equal(call('TickCompleted'), true);
assert.equal(await wait('AwaitTick'), 1);
call('BeginTick'); assert.equal(call('TickCompleted'), false);
assert.throws(() => call('BeginTick'), /InvalidOperationException/);
clock.advance(10); await pump(); assert.equal(call('TickCompleted'), true);
assert.throws(() => call('BeginTick'), /InvalidOperationException/);
assert.equal(await wait('AwaitTick'), 1);
call('DisposePeriodic'); clean(); mark('coalesced-tick-single-consumer');

call('Reset'); call('StartPeriodic', [10n]); call('BeginTick'); call('Cancel');
assert.equal(await wait('AwaitTick'), -3); assert.equal(runtimeInfo().activeTimers, 1);
call('RenewToken'); call('BeginTick'); clock.advance(10); assert.equal(await wait('AwaitTick'), 1);
call('DisposePeriodic'); clean(); mark('tick-cancellation-is-not-timer-cancellation');

call('Reset'); call('StartPeriodic', [10n]); call('ChangePeriod', [30n]); call('BeginTick');
clock.advance(10); await pump(); assert.equal(call('TickCompleted'), false);
clock.advance(20); await pump(); call('DisposePeriodic');
assert.equal(await wait('AwaitTick'), 0); call('BeginTick'); assert.equal(await wait('AwaitTick'), 0);
clean(); mark('period-change-and-disposal-voids-tick');

call('Reset'); call('InlineDelay'); assert.equal(call('InlineDelayDone'), true);
assert.equal(await wait('AwaitWait'), 42); assert.equal(call('ProviderDisposals'), 1);
clean(); mark('synchronous-provider-delay');

call('Reset'); call('InlineTimeout'); assert.equal(await wait('AwaitWait'), -2);
assert.equal(call('ProviderDelayTicks'), 100000n); assert.equal(call('ProviderDisposals'), 1);
assert.equal(call('InputCompleted'), false); detached(); clean(); mark('synchronous-provider-timeout');

call('Reset'); call('ProviderCompletesInput'); assert.equal(await wait('AwaitWait'), 73);
assert.equal(call('ProviderDisposals'), 1); detached(); clean(); mark('provider-reentrant-input-completion');

call('Reset'); assert.equal(call('ThrowingProvider'), true); detached(); clean(); mark('provider-failure-detaches-observer');

call('Reset'); call('CustomCancellation'); assert.equal(call('ProviderDelayTicks'), 109999n);
assert.equal(call('TryResetSource'), false); call('ProviderFire'); assert.equal(call('SourceCanceled'), true);
assert.equal(call('ProviderDisposals'), 1); call('CloseSource'); clean(); mark('custom-cts-original-duration-and-reset');

call('Reset'); assert.equal(call('NativeTimerProviderReset'), false); clean(); mark('custom-provider-native-timer-not-resettable');

const before = call('Timestamp'); clock.advance(7);
assert.equal(call('Timestamp') - before, 70000n); assert.equal(call('TimestampFrequency'), 10000000n);
clean(); mark('monotonic-provider-timestamp');

call('Reset'); setClock();
assert.equal(await wait('Delay', [5n]), 42);
assert.equal(await wait('TimedWait'), 9); assert.equal(call('InputCompleted'), false);
assert.equal(await wait('DurationCancellation'), 11); assert.equal(await wait('NativePeriodic'), 3);
clean(); mark('native-event-loop-time-apis');
console.log(JSON.stringify({cases, passed: cases}));
