import assert from 'node:assert/strict';
import {invoke, invokeAsync, stream, runtimeInfo, setClock} from './library.mjs';
const call = (name, args = []) => invoke('TimerKernel::' + name, args);
const asyncCall = (name, args = [], options = {}) => invokeAsync('TimerKernel::' + name, args, options);
const pause = () => new Promise(resolve => setTimeout(resolve, 0));
const fast = {yieldHost: async () => {}, maxSteps: 128};
const cases = [];
function check(name) { cases.push(name); }
function clean() {
    const info = runtimeInfo();
    assert.equal(info.activeTimers, 0); assert.equal(info.readyTimers, 0);
    assert.equal(info.hostWaiters, 0); assert.equal(info.activeStreams, 0);
}
async function until(predicate) {
    for (let step = 0; step < 100; step++) { if (predicate()) return; await pause(); }
    throw new Error('Host operation never reached expected state');
}
class ManualClock {
    time = 0; events = [];
    now() { return this.time; }
    schedule(milliseconds, callback) {
        const event = {due: this.time + milliseconds, callback, canceled: false, fired: false};
        this.events.push(event); return event;
    }
    cancel(event) { event.canceled = true; }
    advance(milliseconds) {
        this.time += milliseconds;
        for (const event of [...this.events]) {
            if (!event.canceled && !event.fired && event.due <= this.time) {
                event.fired = true; event.callback();
            }
        }
    }
}
const clock = new ManualClock(); setClock(clock);
call('Reset'); call('ScheduleCancel', [10]); const staleReset = clock.events.at(-1);
assert.equal(call('TryResetSource'), true); clean(); staleReset.callback();
await asyncCall('YieldOne', [], fast); assert.equal(call('IsCanceled'), false);
call('ManualCancel'); assert.equal(call('CallbackCount'), 0); call('Close');
check('reset-before-ready-and-stale-callback');

call('Reset'); call('ScheduleCancel', [10]); clock.advance(10);
assert.equal(call('IsCanceled'), false); assert.equal(runtimeInfo().readyTimers, 1);
assert.equal(call('TryResetSource'), false); assert.equal(call('CallbackCount'), 0);
await asyncCall('YieldOne', [], fast);
assert.equal(call('IsCanceled'), true); assert.equal(call('CallbackCount'), 1); clean(); call('Close');
check('queued-reset-fails-without-erasing-callback');

call('Reset'); call('ScheduleCancel', [5]); clock.advance(5); call('ScheduleCancel', [-1]);
assert.equal(call('TryResetSource'), false); await asyncCall('YieldOne', [], fast);
assert.equal(call('CallbackCount'), 1); clean(); call('Close');
check('disarm-does-not-retract-queued-cancellation');

call('Reset'); call('ScheduleCancel', [5]); const staleChange = clock.events.at(-1);
call('ScheduleCancel', [20]); clock.advance(5); staleChange.callback();
await asyncCall('YieldOne', [], fast); assert.equal(call('IsCanceled'), false);
clock.advance(15); await asyncCall('YieldOne', [], fast);
assert.equal(call('CallbackCount'), 1); clean(); call('Close');
check('reschedule-invalidates-native-generation');

for (const ready of [false, true]) {
    call('Reset'); call('BeginDelay', [10]); const staleDelay = clock.events.at(-1);
    if (ready) clock.advance(10);
    assert.equal(call('PendingCompleted'), false); call('ManualCancel');
    assert.equal(call('PendingCanceled'), true); assert.equal(await asyncCall('AwaitPending', [], fast), 9);
    staleDelay.callback(); clean(); call('Close');
}
check('delay-cancellation-before-and-after-readiness');

call('Reset'); call('BeginDelay', [10]); clock.advance(10);
assert.equal(await asyncCall('AwaitPending', [], fast), 7); clean();
const source = call('GetSource');
const list = Object.entries(source.fields).find(([key]) => key.endsWith('::_callbacks'))[1];
const head = Object.values(list.fields).find(value => value !== null);
assert.equal(Object.entries(head.fields).find(([key]) => key.endsWith('::Next'))[1], null);
call('ManualCancel'); assert.equal(call('PendingCanceled'), false); assert.equal(call('CallbackCount'), 1); call('Close');
check('completed-delay-detaches-registration');

call('Reset'); call('BeginDelay', [100]);
const first = asyncCall('AwaitPending'); const second = asyncCall('AwaitPending');
await until(() => runtimeInfo().hostWaiters === 2); clock.advance(100);
assert.deepEqual(await Promise.all([first, second]), [7, 7]); clean(); call('Close');
check('broadcast-wakes-two-managed-task-waiters');

call('Reset'); call('BeginDelay', [100]); const controller = new AbortController();
const aborted = asyncCall('AwaitPending', [], {signal: controller.signal});
const rejection = assert.rejects(aborted, /host-abort/);
await until(() => runtimeInfo().hostWaiters === 1); controller.abort(new Error('host-abort'));
await rejection; assert.equal(runtimeInfo().hostWaiters, 0);
// A host waiter does not own the exported Task: cancellation is a separate managed action.
assert.equal(call('PendingCompleted'), false); call('ManualCancel');
assert.equal(await asyncCall('AwaitPending', [], fast), 9); clean(); call('Close');
check('aborted-host-wait-detaches-without-canceling-task');

assert.equal(call('StressCancellation'), 128); clean();
for (const event of clock.events) event.callback(); clean();
check('bulk-cancellation-cleans-timers-and-stale-notifications');
setClock();
assert.equal(await asyncCall('Delayed', [10]), 42);
assert.equal(await asyncCall('TimedCancel'), 9); clean();
check('native-timer-delay-and-ctor-cancellation');

const disposed = call('DisposedStreams');
const values = stream('TimerKernel::Values', [10000]);
const move = values.next(); const observedMove = move.catch(error => error);
await until(() => runtimeInfo().hostWaiters === 1);
await values.return(); await observedMove;
assert.equal(call('DisposedStreams'), disposed + 1); clean();
check('close-during-delay-drains-async-finally');

const completed = [];
for await (const value of stream('TimerKernel::Values', [2])) completed.push(value);
assert.deepEqual(completed, [11, 22]); clean();
check('native-stream-normal-completion');
console.log(JSON.stringify({cases: cases.length, passed: cases.length, names: cases}));
