import asyncio
import json
from library import invoke, invoke_async, runtime_info, set_clock

def call(name, args=()): return invoke('TimeKernel::' + name, args)
async def wait(name, args=()): return await invoke_async('TimeKernel::' + name, args)
async def pump(): return await wait('Pump')
def field(obj, name): return next((v for k, v in obj.fields.items() if k.endswith('::' + name)), None)
def clean():
    info = runtime_info()
    assert all(info[key] == 0 for key in ('activeTimers', 'readyTimers', 'hostWaiters', 'activeStreams')), info
def detached():
    assert field(call('GetInput'), '_observers') is None
    callbacks = field(call('GetSource'), '_callbacks')
    assert callbacks is None or field(callbacks, 'Head') is None

def rejects(action):
    try: action()
    except Exception as error: assert 'InvalidOperationException' in str(error), str(error)
    else: raise AssertionError('Overlapping tick consumption accepted')

class ManualClock:
    def __init__(self): self.time, self.events = 0, []
    def now(self): return self.time
    def schedule(self, ms, callback):
        event = dict(due=self.time + ms, callback=callback, active=True)
        self.events.append(event)
        return event
    def cancel(self, event): event['active'] = False
    def advance(self, ms):
        self.time += ms
        for event in tuple(self.events):
            if event['active'] and event['due'] <= self.time:
                event['active'] = False
                event['callback']()

async def main():
    cases = 0
    def mark(name):
        nonlocal cases
        cases += 1
        print('PASS time/' + name)
    clock = ManualClock(); set_clock(clock)
    for mode, expected in [('success', 73), ('timeout', -2), ('cancel', -3), ('fault', -5)]:
        call('Reset'); call('StartWait', [100000]); assert runtime_info()['activeTimers'] == 1
        if mode == 'success': call('CompleteInput')
        elif mode == 'timeout': clock.advance(10)
        elif mode == 'cancel': call('Cancel')
        else: call('FailInput')
        assert await wait('AwaitWait') == expected
        if mode in ('timeout', 'cancel'): assert not call('InputCompleted')
        detached(); clean(); mark('wait-' + mode)
    call('Reset'); call('StartWait', [109999])
    assert clock.events[-1]['due'] - clock.time == 10
    clock.advance(10); assert await wait('AwaitWait') == -2
    clean(); mark('duration-truncation')

    call('Reset'); long_delay = asyncio.create_task(wait('Delay', [4294967294]))
    await asyncio.sleep(0)
    assert clock.events[-1]['due'] - clock.time == 4294967294
    clock.advance(2147483647); assert runtime_info()['readyTimers'] == 0
    clock.advance(2147483647); assert await long_delay == 42
    clean(); mark('uint-range-delay')

    call('Reset'); call('StartTimer', [10, 10]); clock.advance(10)
    assert call('Count') == 0
    await pump(); assert call('Count') == 1
    clock.advance(50); await pump(); assert call('Count') == 2
    call('ChangeTimer', [-1, 0]); clock.advance(100); await pump(); assert call('Count') == 2
    call('DisposeTimer'); clean(); mark('serialized-periodic-timer')

    call('Reset'); call('CallbackChange'); clock.advance(10); await pump()
    assert call('Count') == 1
    clock.advance(10); await pump(); assert call('Count') == 1
    clock.advance(15); await pump(); assert call('Count') == 2
    clock.advance(100); await pump(); assert call('Count') == 2
    call('DisposeTimer'); clean(); mark('reentrant-change-wins')

    call('Reset'); call('CallbackDisposal'); clock.advance(10); await pump()
    assert call('DisposalPendingInsideCallback') and await wait('ObserveDisposal')
    assert call('Count') == 1
    clock.advance(100); await pump(); assert call('Count') == 1
    clean(); mark('dispose-async-waits-for-callback-exit')

    call('Reset'); call('CallbackOnlyConstructor'); clock.advance(5); await pump()
    assert call('Count') == 1 and await wait('DrainTimer')
    clean(); mark('callback-only-state')

    call('Reset'); call('StartTimer', [0, 0]); assert call('Count') == 0
    clock.advance(0); await pump(); assert call('Count') == 1
    clock.advance(100); await pump(); assert call('Count') == 1
    call('DisposeTimer'); clean(); mark('zero-period-single-shot')

    call('Reset'); call('StartPeriodic', [10]); clock.advance(100); await pump()
    call('BeginTick'); assert call('TickCompleted') and await wait('AwaitTick') == 1
    call('BeginTick'); assert not call('TickCompleted'); rejects(lambda: call('BeginTick'))
    clock.advance(10); await pump(); assert call('TickCompleted'); rejects(lambda: call('BeginTick'))
    assert await wait('AwaitTick') == 1
    call('DisposePeriodic'); clean(); mark('coalesced-tick-single-consumer')

    call('Reset'); call('StartPeriodic', [10]); call('BeginTick'); call('Cancel')
    assert await wait('AwaitTick') == -3 and runtime_info()['activeTimers'] == 1
    call('RenewToken'); call('BeginTick'); clock.advance(10); assert await wait('AwaitTick') == 1
    call('DisposePeriodic'); clean(); mark('tick-cancellation-is-not-timer-cancellation')

    call('Reset'); call('StartPeriodic', [10]); call('ChangePeriod', [30]); call('BeginTick')
    clock.advance(10); await pump(); assert not call('TickCompleted')
    clock.advance(20); await pump(); call('DisposePeriodic')
    assert await wait('AwaitTick') == 0
    call('BeginTick'); assert await wait('AwaitTick') == 0
    clean(); mark('period-change-and-disposal-voids-tick')

    call('Reset'); call('InlineDelay'); assert call('InlineDelayDone')
    assert await wait('AwaitWait') == 42 and call('ProviderDisposals') == 1
    clean(); mark('synchronous-provider-delay')

    call('Reset'); call('InlineTimeout'); assert await wait('AwaitWait') == -2
    assert call('ProviderDelayTicks') == 100000 and call('ProviderDisposals') == 1
    assert not call('InputCompleted'); detached(); clean(); mark('synchronous-provider-timeout')

    call('Reset'); call('ProviderCompletesInput'); assert await wait('AwaitWait') == 73
    assert call('ProviderDisposals') == 1
    detached(); clean(); mark('provider-reentrant-input-completion')

    call('Reset'); assert call('ThrowingProvider')
    detached(); clean(); mark('provider-failure-detaches-observer')

    call('Reset'); call('CustomCancellation'); assert call('ProviderDelayTicks') == 109999
    assert not call('TryResetSource')
    call('ProviderFire'); assert call('SourceCanceled') and call('ProviderDisposals') == 1
    call('CloseSource'); clean(); mark('custom-cts-original-duration-and-reset')

    call('Reset'); assert not call('NativeTimerProviderReset')
    clean(); mark('custom-provider-native-timer-not-resettable')

    before = call('Timestamp'); clock.advance(7)
    assert call('Timestamp') - before == 70000 and call('TimestampFrequency') == 10000000
    clean(); mark('monotonic-provider-timestamp')

    call('Reset'); set_clock()
    assert await wait('Delay', [5]) == 42
    assert await wait('TimedWait') == 9 and not call('InputCompleted')
    assert await wait('DurationCancellation') == 11 and await wait('NativePeriodic') == 3
    clean(); mark('native-event-loop-time-apis')
    print(json.dumps(dict(cases=cases, passed=cases)))

asyncio.run(main())
