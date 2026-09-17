# Native async iterator/context manager. Managed StreamCursor<T> owns and consumes each operation.
import asyncio as _asyncio


class StreamCancelledError(Exception):
    pass


class StreamCleanupPendingError(TimeoutError):
    def __init__(self, phase):
        super().__init__('Stream cleanup is still pending (' + phase + '); complete the operation and retry aclose()')
        self.phase = phase


class ManagedAsyncIterator:
    def __init__(self, runtime, name, args, max_steps, cleanup_steps, yield_host):
        self.runtime, self.name, self.args = runtime, name, list(args)
        self.max_steps = max_steps
        self.cleanup_steps = max_steps if cleanup_steps is None else cleanup_steps
        if any(type(n) is not int or n < 1 or n > 9007199254740991 for n in (self.max_steps, self.cleanup_steps)):
            raise ValueError('Stream step budgets must be positive safe integers')
        if yield_host is not None and not callable(yield_host): raise TypeError('yield_host must be callable')
        self.yield_host = yield_host or (lambda: _asyncio.sleep(0))
        candidates = [(k, v) for k, v in runtime.meta['exports'].items() if k == name or k.split('(')[0] == name]
        if len(candidates) != 1: raise ValueError('Use an unambiguous exported stream signature: ' + name)
        method = runtime.meta['methods'][candidates[0][1]]
        if len(method['params']) != len(self.args): raise ValueError('Incorrect stream argument count')
        self.binding = runtime.meta['streamBindings'].get(method['returns'])
        if not self.binding: raise TypeError('Export requires a linked IAsyncEnumerable<T> stream contract')
        self.cursor = None
        self.closed = self.stopping = self.cancel_sent = False
        self.reason = self.cancel_error = None
        self._active_task = self._move_done = self._loop = None
        self._close_lock = _asyncio.Lock()

    def __aiter__(self): return self
    async def __aenter__(self): return self
    async def __aexit__(self, *_): await self.aclose(); return False

    @property
    def pending(self):
        if self.cursor is None: return None
        return 'move' if self.call('get_MovePending') else 'dispose' if self.call('get_DisposePending') else None

    def call(self, member): return self.runtime.call(self.binding['methods'][member], [self.cursor])

    def _check_loop(self):
        loop = _asyncio.get_running_loop()
        if self._loop is not None and self._loop is not loop: raise RuntimeError('A stream belongs to one event loop')
        self._loop = loop

    def _request_cancel(self):
        if self.cursor is None or self.cancel_sent: return
        self.cancel_sent = True
        try: self.call('Cancel')
        except Exception as error: self.cancel_error = error

    def cancel(self):
        if self.closed or self.reason is not None: return False
        self.reason = StreamCancelledError('Stream enumeration was canceled')
        self._request_cancel()
        return True

    def _check_abort(self):
        if self.cancel_error is not None: raise self.cancel_error
        if self.reason is not None: raise self.reason

    def _open(self):
        if self.cursor is not None: return
        enumerable = self.runtime.invoke_export(self.name, self.args)
        self.cursor = self.runtime.call(self.binding['methods']['Open'], [enumerable])
        self.args = None
        self.runtime.active_streams += 1

    def _observe_closed(self):
        if self.cursor is not None and self.call('get_IsClosed'):
            self.cursor = None
            self.runtime.active_streams -= 1
        if self.cursor is None:
            self.closed = True
            self.args = self.yield_host = None

    async def _wait(self, member, steps, cleanup):
        for step in range(steps + 1):
            if not cleanup: self._check_abort()
            if self.call(member): return
            if step == steps: raise TimeoutError('Stream operation exceeded its pump step budget')
            self.runtime.call(self.binding['pump'], [])
            await self.yield_host()

    async def _drain_dispose(self):
        self.stopping = True
        if self.cursor is None: self._observe_closed(); return
        try:
            if self.call('get_MovePending'):
                self._request_cancel()
                try: await self._wait('get_MoveCompleted', self.cleanup_steps, True)
                except BaseException as error: raise StreamCleanupPendingError('move') from error
                try: self.call('FinishMove')
                except CliError: pass  # Closing discards a pending item/cancellation but consumes it once.
            self.call('StartDispose')
            try: await self._wait('get_DisposeCompleted', self.cleanup_steps, True)
            except BaseException as error: raise StreamCleanupPendingError('dispose') from error
            self.call('FinishDispose')
            if self.cancel_error is not None: raise self.cancel_error
        finally: self._observe_closed()

    async def __anext__(self):
        self._check_loop()
        if self._active_task is not None: raise RuntimeError('Overlapping stream __anext__ calls are not allowed')
        if self.closed: raise StopAsyncIteration
        if self.stopping: raise RuntimeError('Stream cleanup must finish before further iteration')
        self._active_task = _asyncio.current_task()
        self._move_done = self._loop.create_future()
        try:
            self._check_abort(); self._open(); self.call('StartMove')
            await self._wait('get_MoveCompleted', self.max_steps, False)
            self._check_abort()
            more = self.call('FinishMove')
            ended = not more or self.stopping
            if ended: await self._drain_dispose()
            else: value = self.runtime.stream_value(self.call('get_Current'), self.binding['element'])
        except BaseException:
            # A cancelled waiter cannot reuse a ValueTask; cleanup failures must not double the budget.
            if not self.stopping: await self._drain_dispose()
            raise
        finally:
            self._active_task = None
            self._move_done.set_result(None)
            self._move_done = None
        if ended: raise StopAsyncIteration
        return value

    async def aclose(self):
        self._check_loop()
        if self._active_task is _asyncio.current_task(): raise RuntimeError('Cannot close reentrantly from the stream yield callback')
        self.stopping = True
        if self._active_task is not None: self._request_cancel()
        async with self._close_lock:
            done = self._move_done
            if done is not None: await _asyncio.shield(done)
            await self._drain_dispose()


class StreamRuntime(ArrayRuntime):
    def __init__(self, metadata, write=None):
        super().__init__(metadata, write)
        self.active_streams = 0

    def stream(self, name, args=(), *, max_steps=100000, cleanup_steps=None, yield_host=None):
        return ManagedAsyncIterator(self, name, args, max_steps, cleanup_steps, yield_host)

    def stream_value(self, value, type_name):
        if isinstance(value, CliString): return value.text
        return bool(value) if type_name == 'System.Boolean' else value

    def runtime_info(self):
        return dict(super().runtime_info(), activeStreams=self.active_streams, streamPolicy='managed-stream-v1')
