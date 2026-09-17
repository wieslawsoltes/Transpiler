import asyncio
import json
from library import invoke, invoke_async, stream, runtime_info, set_clock

def call(name, args=()): return invoke('TimerKernel::' + name, args)
async def acall(name, args=()): return await invoke_async('TimerKernel::' + name, args, max_steps=128)

def clean():
    info = runtime_info()
    assert all(info[key] == 0 for key in ('activeTimers', 'readyTimers', 'hostWaiters', 'activeStreams')), info

async def until(predicate):
    for _ in range(100):
        if predicate(): return
        await asyncio.sleep(0.001)
    raise AssertionError('Host operation never reached expected state')

class ManualClock:
    def __init__(self): self.time, self.events = 0, []
    def now(self): return self.time
    def schedule(self, milliseconds, callback):
        event = dict(due=self.time + milliseconds, callback=callback, canceled=False, fired=False)
        self.events.append(event)
        return event
    def cancel(self, event): event['canceled'] = True
    def advance(self, milliseconds):
        self.time += milliseconds
        for event in list(self.events):
            if not event['canceled'] and not event['fired'] and event['due'] <= self.time:
                event['fired'] = True
                event['callback']()

async def main():
    cases = []
    clock = ManualClock(); set_clock(clock)
    call('Reset'); call('ScheduleCancel', [10]); stale = clock.events[-1]
    assert call('TryResetSource') is True
    clean(); stale['callback']()
    await acall('YieldOne'); assert call('IsCanceled') is False
    call('ManualCancel'); assert call('CallbackCount') == 0; call('Close')
    cases.append('reset-before-ready-and-stale-callback')

    call('Reset'); call('ScheduleCancel', [10]); clock.advance(10)
    assert call('IsCanceled') is False and runtime_info()['readyTimers'] == 1
    assert call('TryResetSource') is False and call('CallbackCount') == 0
    await acall('YieldOne')
    assert call('IsCanceled') is True and call('CallbackCount') == 1
    clean(); call('Close'); cases.append('queued-reset-fails-without-erasing-callback')

    call('Reset'); call('ScheduleCancel', [5]); clock.advance(5); call('ScheduleCancel', [-1])
    assert call('TryResetSource') is False
    await acall('YieldOne'); assert call('CallbackCount') == 1
    clean(); call('Close'); cases.append('disarm-does-not-retract-queued-cancellation')

    call('Reset'); call('ScheduleCancel', [5]); stale = clock.events[-1]
    call('ScheduleCancel', [20]); clock.advance(5); stale['callback']()
    await acall('YieldOne'); assert call('IsCanceled') is False
    clock.advance(15); await acall('YieldOne'); assert call('CallbackCount') == 1
    clean(); call('Close'); cases.append('reschedule-invalidates-native-generation')

    for ready in (False, True):
        call('Reset'); call('BeginDelay', [10]); stale = clock.events[-1]
        if ready: clock.advance(10)
        assert call('PendingCompleted') is False
        call('ManualCancel'); assert call('PendingCanceled') is True
        assert await acall('AwaitPending') == 9
        stale['callback'](); clean(); call('Close')
    cases.append('delay-cancellation-before-and-after-readiness')

    call('Reset'); call('BeginDelay', [10]); clock.advance(10)
    assert await acall('AwaitPending') == 7
    clean()
    source = call('GetSource')
    callbacks = next(v for k,v in source.fields.items() if k.endswith('::_callbacks'))
    head = next(v for v in callbacks.fields.values() if v is not None)
    assert next(v for k,v in head.fields.items() if k.endswith('::Next')) is None
    call('ManualCancel'); assert call('PendingCanceled') is False and call('CallbackCount') == 1
    call('Close'); cases.append('completed-delay-detaches-registration')

    call('Reset'); call('BeginDelay', [100])
    first = asyncio.create_task(acall('AwaitPending')); second = asyncio.create_task(acall('AwaitPending'))
    await until(lambda: runtime_info()['hostWaiters'] == 2); clock.advance(100)
    assert await asyncio.gather(first, second) == [7, 7]
    clean(); call('Close'); cases.append('broadcast-wakes-two-managed-task-waiters')

    call('Reset'); call('BeginDelay', [100])
    aborted = asyncio.create_task(acall('AwaitPending'))
    await until(lambda: runtime_info()['hostWaiters'] == 1)
    aborted.cancel()
    try: await aborted
    except asyncio.CancelledError: pass
    else: raise AssertionError('Expected canceled host waiter')
    assert runtime_info()['hostWaiters'] == 0 and call('PendingCompleted') is False
    call('ManualCancel'); assert await acall('AwaitPending') == 9
    clean(); call('Close'); cases.append('aborted-host-wait-detaches-without-canceling-task')

    assert call('StressCancellation') == 128
    clean()
    for event in clock.events: event['callback']()
    clean(); cases.append('bulk-cancellation-cleans-timers-and-stale-notifications')
    set_clock()
    assert await acall('Delayed', [10]) == 42
    assert await acall('TimedCancel') == 9
    clean(); cases.append('native-timer-delay-and-ctor-cancellation')

    disposed = call('DisposedStreams')
    values = stream('TimerKernel::Values', [10000])
    move = asyncio.create_task(anext(values))
    await until(lambda: runtime_info()['hostWaiters'] == 1)
    await values.aclose()
    try: await move
    except Exception: pass
    assert call('DisposedStreams') == disposed + 1
    clean(); cases.append('close-during-delay-drains-async-finally')

    completed = [value async for value in stream('TimerKernel::Values', [2])]
    assert completed == [11, 22]
    clean(); cases.append('native-stream-normal-completion')
    print(json.dumps(dict(cases=len(cases), passed=len(cases), names=cases)))

asyncio.run(main())
