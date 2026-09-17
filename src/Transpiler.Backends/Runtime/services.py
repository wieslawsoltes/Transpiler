# Host lifetime and clock services. Only Scheduler.RunOne executes managed callbacks.
import weakref as _weakref
import math as _clock_math
import time as _clock_time


class NativeHostClock:
    def __init__(self):
        self.loop, self.handles = None, set()

    def now(self):
        return _clock_time.monotonic() * 1000

    def schedule(self, milliseconds, callback):
        import asyncio
        loop = asyncio.get_running_loop()
        if self.handles and loop is not self.loop:
            raise RuntimeError('Active native timers belong to another asyncio loop')
        self.loop = loop
        def notify():
            self.handles.discard(handle)
            callback()
        handle = loop.call_later(milliseconds / 1000, notify)
        self.handles.add(handle)
        return handle

    def cancel(self, handle):
        self.handles.discard(handle)
        handle.cancel()


class HostClockService:
    def __init__(self):
        self.timers, self.notifications, self.waiters = {}, {}, set()
        self.next_id, self.version, self.failure = 1, object(), None
        self.install(None)

    def install(self, adapter):
        if self.timers or self.waiters:
            raise RuntimeError('Cannot replace a clock with live timers or waiters')
        if adapter is None:
            adapter = NativeHostClock()
        if any(not callable(getattr(adapter, name, None)) for name in ('now', 'schedule', 'cancel')):
            raise TypeError('Clock requires now(), schedule(milliseconds, callback), and cancel(handle)')
        self.adapter, self.last_now, self.failure = adapter, float('-inf'), None

    def now(self):
        value = self.adapter.now()
        if type(value) not in (int, float) or not _clock_math.isfinite(value) or value < self.last_now:
            raise RuntimeError('Host clock must return finite, monotonic milliseconds')
        self.last_now = value
        return value

    @staticmethod
    def delay(value):
        if type(value) is not int or value < -1 or value > 2147483647:
            raise ValueError('Invalid timer delay')

    def get(self, handle):
        if handle not in self.timers:
            raise ValueError('Unknown or disposed timer handle')
        return self.timers[handle]

    def create(self, milliseconds, callback):
        self.delay(milliseconds)
        if callback is None:
            raise TypeError('Timer callback is required')
        if self.next_id > 2147483647:
            raise OverflowError('Timer handle budget exhausted')
        timer = dict(id=self.next_id, callback=callback, native=None, generation=object(),
                     deadline=None, ready=False, everQueued=False)
        self.next_id += 1
        self.timers[timer['id']] = timer
        try:
            self.change(timer['id'], milliseconds)
        except BaseException:
            self.destroy(timer['id'])
            raise
        return timer['id']

    def cancel_native(self, timer):
        timer['generation'] = object()
        native, timer['native'] = timer['native'], None
        if native is not None and native['armed']:
            self.adapter.cancel(native['handle'])

    def schedule(self, timer):
        generation = timer['generation'] = object()
        pending = timer['native'] = dict(armed=False)
        def notify():
            if self.timers.get(timer['id']) is not timer or timer['generation'] is not generation:
                return
            timer['generation'] = object()  # Ignore duplicate native notifications.
            timer['native'] = None
            try:
                if self.now() >= timer['deadline']:
                    timer['ready'] = timer['everQueued'] = True
                self.notifications[timer['id']] = timer
            except Exception as error:
                self.failure = error
            self.signal()
        try:
            handle = self.adapter.schedule(max(0, timer['deadline'] - self.now()), notify)
        except BaseException:
            timer['native'] = None
            raise
        if timer['native'] is pending:
            pending.update(handle=handle, armed=True)
        else:
            self.adapter.cancel(handle)

    def change(self, handle, milliseconds):
        self.delay(milliseconds)
        timer = self.get(handle)
        self.cancel_native(timer)
        if not timer['ready']:
            self.notifications.pop(handle, None)
        timer['deadline'] = None if milliseconds == -1 else self.now() + milliseconds
        if timer['deadline'] is not None:
            self.schedule(timer)
        self.signal()

    def destroy(self, handle):
        timer = self.timers.pop(handle, None)
        if timer is None:
            return
        self.notifications.pop(handle, None)
        timer['callback'] = None
        try:
            self.cancel_native(timer)
        finally:
            self.signal()

    def take_ready(self):
        if self.failure is not None:
            raise self.failure
        for _ in range(len(self.notifications)):
            handle = next(iter(self.notifications))
            timer = self.notifications.pop(handle)
            if timer['ready']:
                timer['ready'] = False
                if timer['native'] is None:
                    timer['deadline'] = None
                return timer['callback']
            if timer['deadline'] is not None and timer['native'] is None:
                self.schedule(timer)
        return None

    def signal(self):
        self.version = object()
        waiters = tuple(self.waiters)
        self.waiters.clear()
        for waiter in waiters:
            if not waiter.done():
                waiter.set_result(None)

    def can_wait(self):
        return bool(self.notifications) or any(t['native'] is not None for t in self.timers.values())

    async def wait(self, version):
        import asyncio
        if self.failure is not None:
            raise self.failure
        if version is not self.version or self.notifications or not self.can_wait():
            return
        loop = asyncio.get_running_loop()
        if isinstance(self.adapter, NativeHostClock) and self.adapter.handles and self.adapter.loop is not loop:
            raise RuntimeError('Active native timers belong to another asyncio loop')
        if any(waiter.get_loop() is not loop for waiter in self.waiters):
            raise RuntimeError('Clock waiters must share one asyncio loop')
        waiter = loop.create_future()
        self.waiters.add(waiter)
        try:
            await waiter
        finally:
            self.waiters.discard(waiter)

    def info(self):
        return dict(clock='host-clock-v1', activeTimers=len(self.timers), readyTimers=len(self.notifications),
                    hostWaiters=len(self.waiters), idlePolicy='timer-notification-or-explicit-yield')


class CliWeak(CliObject):
    __slots__ = ('reference',)
    def __init__(self, type_name, target):
        super().__init__(type_name)
        self.reference = None if target is None else _weakref.ref(target)


class HostedRuntime(ManagedRuntime):
    def __init__(self, metadata, write=None):
        super().__init__(metadata, write)
        self.parents['System.ObjectDisposedException'] = 'System.InvalidOperationException'
        self._identity_hashes = _weakref.WeakKeyDictionary()
        self._next_hash = 1
        self._roots, self._next_root = {}, 1
        self.clock = HostClockService()

    def set_clock(self, adapter):
        self.clock.install(adapter)

    def new_object(self, method_id, args):
        method = self.meta['methods'][method_id]
        if method['intrinsic'] == 'weak.ctor': return CliWeak(method['type'], args[0])
        return super().new_object(method_id, args)

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'clock.create': return self.clock.create(args[0], self.nonnull(args[1]))
        if op == 'clock.change': return self.clock.change(args[0], args[1])
        if op == 'clock.fired': return int(self.clock.get(args[0])['everQueued'])
        if op == 'clock.destroy': return self.clock.destroy(args[0])
        if op == 'clock.take': return self.clock.take_ready()
        if op == 'clock.signal': return self.clock.signal()
        if op == 'gc.keep-alive':
            # The actual argument remains a strong Python reference through this call boundary.
            return None
        if op == 'object.identity-hash':
            value = args[0]
            if value is None: return 0
            if value not in self._identity_hashes:
                self._identity_hashes[value] = self._next_hash
                self._next_hash = (self._next_hash + 1) & 0x7fffffff or 1
            return self._identity_hashes[value]
        if op == 'weak.set':
            self.nonnull(args[0]).reference = None if args[1] is None else _weakref.ref(args[1])
            return None
        if op == 'weak.get':
            reference = self.nonnull(args[0]).reference
            value = None if reference is None else reference()
            self.nonnull(args[1]).set(value)
            return int(value is not None)
        return super().external(method, args)

    async def await_export(self, name, args, max_steps=100000):
        import asyncio
        if type(max_steps) is not int or max_steps < 1: raise ValueError('max_steps must be a positive integer')
        candidates = [(k, v) for k, v in self.meta['exports'].items() if k == name or k.split('(')[0] == name]
        if len(candidates) != 1: raise ValueError('Use an unambiguous exported method signature')
        result_type = self.meta['methods'][candidates[0][1]]['returns']
        binding = self.meta['asyncBindings'].get(result_type)
        value = self.invoke_export(name, args)
        if binding is None: return value
        receiver = self.cell_ref([value], 0, result_type) if binding.get('valueType') else value
        for step in range(max_steps + 1):
            version = self.clock.version
            if self.call(binding['completed'], [receiver]):
                cell = [self.call(binding['getAwaiter'], [receiver])]
                result = self.call(binding['getResult'], [self.cell_ref(cell, 0, binding['awaiterType'])])
                if isinstance(result, CliString): return result.text
                return bool(result) if binding['resultType'] == 'System.Boolean' else result
            if step == max_steps:
                raise TimeoutError('Cooperative task exceeded the host pump step budget')
            progressed = self.call(binding['pump'], [])
            if not progressed and self.clock.can_wait():
                await self.clock.wait(version)
            else:
                await asyncio.sleep(0)

    def retain(self, value):
        if self._next_root > 9007199254740991: raise OverflowError('Root handle budget exhausted')
        handle = self._next_root
        self._next_root += 1
        self._roots[handle] = value
        return handle

    def dereference_root(self, handle):
        if type(handle) is not int or handle not in self._roots: raise ValueError('Unknown or released root handle')
        return self._roots[handle]

    def release(self, handle):
        if type(handle) is not int: raise ValueError('Root handle must be an integer')
        if handle not in self._roots: return False
        del self._roots[handle]
        return True

    def runtime_info(self):
        return {'heap': 'host-gc', 'weakReferences': True, 'explicitRoots': len(self._roots),
                'forcedCollection': False, 'managedFinalizers': False, 'pinning': False,
                'scheduler': 'cooperative-fifo-single-thread', **self.clock.info()}


def set_clock(adapter=None):
    R.set_clock(adapter)
