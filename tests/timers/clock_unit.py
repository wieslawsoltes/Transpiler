"""Execute the shipped clock layer directly; no compiler or .NET SDK is needed."""
import asyncio
import json
from pathlib import Path
scope = {}
source = (Path(__file__).resolve().parents[2] / 'src/Transpiler.Backends/Runtime/services.py').read_text()
exec(compile(source.split('class CliWeak')[0], 'services.py', 'exec'), scope)
HostClockService = scope['HostClockService']

class ManualClock:
    def __init__(self): self.time, self.events = 0, []
    def now(self): return self.time
    def schedule(self, ms, callback):
        event = dict(due=self.time + ms, callback=callback, active=True)
        self.events.append(event)
        return event
    def cancel(self, event): event['active'] = False
    def fire(self, event): event['active'] = False; event['callback']()
    def advance(self, ms):
        self.time += ms
        for event in tuple(self.events):
            if event['active'] and event['due'] <= self.time: self.fire(event)

def setup():
    service, clock = HostClockService(), ManualClock()
    service.install(clock)
    return service, clock

def raises(action, message=None):
    try: action()
    except Exception as error:
        if message: assert message in str(error), str(error)
        return
    raise AssertionError('Expected an exception')

async def main():
    cases = 0
    def passed(name):
        nonlocal cases
        cases += 1
        print('PASS clock/' + name)
    s, c = setup(); callback = object(); handle = s.create(10, callback)
    c.fire(c.events[0]); assert not s.get(handle)['everQueued']; assert s.take_ready() is None
    assert len(c.events) == 2; c.advance(10); assert s.take_ready() is callback; assert s.take_ready() is None; s.destroy(handle)
    passed('early-notification')
    s, c = setup(); callback = object(); handle = s.create(10, callback)
    c.advance(10); s.change(handle, -1); assert s.get(handle)['everQueued']; assert s.take_ready() is callback; s.destroy(handle)
    passed('queued-reset-history')
    s, c = setup(); callback = object(); handle = s.create(10, callback); old = c.events[0]
    s.change(handle, 20); c.advance(10); c.fire(old); assert s.take_ready() is None; assert not s.get(handle)['everQueued']
    c.advance(10); current = c.events[1]; c.fire(current); assert s.take_ready() is callback
    c.fire(current); assert s.take_ready() is None; s.destroy(handle)
    passed('stale-and-duplicate-callbacks')
    s, c = setup(); handle = s.create(0, object()); record = s.get(handle)
    c.advance(0); s.destroy(handle); s.destroy(handle); c.fire(c.events[0])
    assert record['callback'] is None and s.take_ready() is None
    assert s.info()['activeTimers'] == s.info()['readyTimers'] == 0
    passed('dispose-removes-roots-and-readiness')
    class SynchronousClock:
        time = schedules = cancels = 0
        def now(self): return self.time
        def schedule(self, _, ready): self.schedules += 1; ready(); return self.schedules
        def cancel(self, _): self.cancels += 1
    s, c = HostClockService(), SynchronousClock(); s.install(c); calls = []
    callback = lambda: calls.append(1); handle = s.create(0, callback)
    assert not calls and c.cancels == 1 and s.take_ready() is callback; s.destroy(handle)
    passed('synchronous-notification-is-not-managed-execution')
    s, c = HostClockService(), SynchronousClock(); s.install(c); callback = object(); handle = s.create(10, callback)
    assert s.take_ready() is None and c.schedules == 2 and not s.get(handle)['everQueued']
    c.time = 10; assert s.take_ready() is None; assert s.take_ready() is callback; s.destroy(handle)
    passed('synchronous-early-notification-is-bounded')
    class BrokenClock(ManualClock):
        def schedule(self, *_): raise RuntimeError('arm failed')
    s = HostClockService(); s.install(BrokenClock()); raises(lambda: s.create(1, object()), 'arm failed'); assert s.info()['activeTimers'] == 0
    passed('failed-arm-releases-record')
    s, _ = setup()
    for n in (-2, 4294967295, 0.5, float('nan'), True): raises(lambda n=n: s.create(n, object()))
    s.next_id = 2147483648; raises(lambda: s.create(1, object()), 'budget'); assert s.info()['activeTimers'] == 0
    passed('range-and-handle-budget')
    s, _ = setup(); handle = s.create(10, object()); version = s.version
    a, b = asyncio.create_task(s.wait(version)), asyncio.create_task(s.wait(version)); await asyncio.sleep(0)
    assert s.info()['hostWaiters'] == 2; s.signal(); await asyncio.gather(a, b)
    assert s.info()['hostWaiters'] == 0; await s.wait(version); assert not s.waiters; s.destroy(handle)
    passed('broadcast-and-generation-wakeup')
    s, _ = setup(); handle = s.create(10, object()); waiter = asyncio.create_task(s.wait(s.version)); await asyncio.sleep(0)
    waiter.cancel()
    try: await waiter
    except asyncio.CancelledError: pass
    else: raise AssertionError('Expected cancellation')
    assert s.info()['hostWaiters'] == 0; s.destroy(handle)
    passed('cancel-detaches-waiter')
    s, _ = setup(); handle = s.create(10, object()); raises(lambda: s.install(None), 'live timers'); s.destroy(handle); s.install(None)
    passed('active-clock-replacement-rejected')
    s, c = setup(); expected = []
    for i in range(256):
        item = object(); handle = s.create(10, item)
        if i % 2: expected.append((handle, item))
        else: s.destroy(handle)
    c.advance(10)
    for handle, item in expected: assert s.take_ready() is item; s.destroy(handle)
    for event in c.events: c.fire(event)
    assert s.take_ready() is None and s.info()['activeTimers'] == s.info()['readyTimers'] == 0
    passed('timer-fifo-and-disposal-stress')
    s = HostClockService(); callback = object(); handle = s.create(20, callback)
    ready = None
    for _ in range(16):
        await s.wait(s.version); ready = s.take_ready()
        if ready is not None: break
    assert ready is callback; s.destroy(handle)
    assert s.info()['hostWaiters'] == s.info()['activeTimers'] == 0
    passed('native-timer-waits-without-polling')
    print(json.dumps(dict(cases=cases, passed=cases)))

if __name__ == '__main__': asyncio.run(main())
