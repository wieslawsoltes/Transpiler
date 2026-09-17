import asyncio
import contextlib
import json
import math
from library import stream, invoke, runtime_info, StreamCleanupPendingError, StreamCancelledError


def i(name, args=()): return invoke('Kernel::' + name, args)
fast = dict(max_steps=32, cleanup_steps=32)

async def collect(values): return [value async for value in values]

async def rejected(awaitable, error_type=Exception, text=None):
    try: await awaitable
    except error_type as error:
        if text is not None: assert text in str(error), str(error)
        return error
    raise AssertionError('Operation should have failed')

async def main():
    cases = 0
    def check():
        nonlocal cases
        assert runtime_info()['activeStreams'] == 0
        cases += 1

    before = i('Factories')
    s = stream('Kernel::Numbers', [3], **fast)
    assert i('Factories') == before
    await s.aclose()
    assert i('Factories') == before and s.closed
    await rejected(anext(s), StopAsyncIteration)
    check()

    before = i('Closed'); s = stream('Kernel::Numbers', [3], **fast)
    assert await collect(s) == [9007199254740993, 9007199254740994, 9007199254740995]
    assert i('Closed') == before + 1 and s.closed
    await s.aclose(); assert i('Closed') == before + 1
    check()

    before = i('Closed')
    async with stream('Kernel::Numbers', [3], **fast) as s:
        async for value in s:
            assert value == 9007199254740993
            break
    assert s.closed and i('Closed') == before + 1
    check()

    before = i('Closed')
    async with contextlib.aclosing(stream('Kernel::Numbers', [3], **fast)) as s:
        async for value in s: break
    assert s.closed and i('Closed') == before + 1
    check()

    before = i('Closed'); body_error = ValueError('body')
    try:
        async with stream('Kernel::Numbers', [3], **fast) as s:
            async for value in s: raise body_error
    except ValueError as error: assert error is body_error
    else: raise AssertionError('Missing body error')
    assert s.closed and i('Closed') == before + 1
    check()

    assert await collect(stream('Kernel::Text', **fast)) == ['A🚂B', None]
    assert await collect(stream('Kernel::Flags', **fast)) == [True, False]
    floats = await collect(stream('Kernel::Floats', **fast))
    assert math.isnan(floats[0]) and math.copysign(1.0, floats[1]) < 0 and floats[2] == math.inf
    s = stream('Kernel::Items', **fast)
    a = await anext(s); key = next(k for k in a.fields if k.endswith('::X')); a.fields[key] = 99
    assert (await anext(s)).fields[key] == 7
    await s.aclose()
    check()

    a = stream('Kernel::Numbers', [2], **fast); b = stream('Kernel::Numbers', [2], **fast)
    assert await asyncio.gather(anext(a), anext(b)) == [9007199254740993, 9007199254740993]
    assert runtime_info()['activeStreams'] == 2
    await asyncio.gather(a.aclose(), b.aclose())
    check()

    s = stream('Kernel::Probe', [0], **fast); assert await anext(s) == 1
    cursor = s.cursor; await s.aclose()
    assert (i('Moves'), i('Reads'), i('Disposes'), i('DisposeReads')) == (1, 1, 1, 1)
    assert all(v is None for k, v in cursor.fields.items() if k.endswith('::_enumerator') or k.endswith('::_cancellation'))
    assert s.cursor is None and s.yield_host is None
    check()

    for mode, part in [(3, 'current fault'), (4, 'move call fault'), (5, 'move result fault')]:
        s = stream('Kernel::Probe', [mode], **fast)
        await rejected(anext(s), text=part)
        assert i('Disposes') == 1 and s.closed
        check()
    for mode, part in [(6, 'dispose call fault'), (7, 'dispose result fault')]:
        s = stream('Kernel::Probe', [mode], **fast); await anext(s)
        await rejected(s.aclose(), text=part)
        assert s.closed
        await s.aclose(); assert i('Disposes') == 1
        check()

    for name in ('Null', 'FactoryFault'):
        s = stream('Kernel::' + name, **fast); await rejected(anext(s)); assert s.closed
    for mode in (10, 11):
        s = stream('Kernel::Probe', [mode], **fast); await rejected(anext(s)); assert s.closed and i('Disposes') == 0
    check()

    sent = False
    async def complete():
        nonlocal sent
        if not sent: sent = True; i('CompleteMove', [True])
        await asyncio.sleep(0)
    s = stream('Kernel::Probe', [1], yield_host=complete, **fast)
    assert await anext(s) == 1
    await s.aclose(); assert i('Moves') == i('Reads') == 1
    check()

    async def cancel(): s.cancel(); await asyncio.sleep(0)
    s = stream('Kernel::Probe', [2], yield_host=cancel, **fast)
    await rejected(anext(s), StreamCancelledError)
    assert (i('Canceled'), i('Reads'), i('Disposes')) == (1, 1, 1)
    check()

    before = i('Factories'); s = stream('Kernel::Numbers', [3], **fast); assert s.cancel()
    await rejected(anext(s), StreamCancelledError)
    assert i('Factories') == before and s.closed
    check()

    entered = asyncio.Event()
    async def cooperate(): entered.set(); await asyncio.sleep(0)
    s = stream('Kernel::Probe', [2], yield_host=cooperate, **fast)
    move = asyncio.create_task(anext(s)); await entered.wait()
    await rejected(anext(s), RuntimeError, 'Overlapping')
    await s.aclose(); await rejected(move)
    assert s.closed and i('Reads') == i('Disposes') == 1
    check()

    # Python Task.cancel must drain the managed move before DisposeAsync, then propagate cancellation.
    entered = asyncio.Event(); s = stream('Kernel::Probe', [2], yield_host=cooperate, **fast)
    move = asyncio.create_task(anext(s)); await entered.wait(); move.cancel()
    await rejected(move, asyncio.CancelledError)
    assert s.closed and i('Reads') == i('Disposes') == 1
    check()

    s = stream('Kernel::Probe', [1], max_steps=2, cleanup_steps=2)
    error = await rejected(anext(s), StreamCleanupPendingError)
    assert error.phase == s.pending == 'move' and not s.closed
    assert i('Reads') == i('Disposes') == 0 and runtime_info()['activeStreams'] == 1
    await rejected(anext(s), RuntimeError, 'cleanup')
    i('CompleteMove', [False]); await s.aclose()
    assert i('Moves') == i('Reads') == i('Disposes') == 1
    check()

    s = stream('Kernel::Probe', [8], max_steps=2, cleanup_steps=2); await anext(s)
    error = await rejected(s.aclose(), StreamCleanupPendingError)
    assert error.phase == s.pending == 'dispose' and i('Disposes') == 1 and i('DisposeReads') == 0
    i('CompleteDispose'); await s.aclose()
    assert s.closed and i('Disposes') == i('DisposeReads') == 1
    check()

    try:
        async with stream('Kernel::Probe', [7], **fast) as s:
            async for value in s: raise ValueError('body')
    except Exception as error: assert 'dispose result fault' in str(error) and isinstance(error.__context__, ValueError)
    else: raise AssertionError('Cleanup error should replace/chains body error in Python')
    check()

    before = i('Factories')
    for options in [dict(max_steps=0), dict(cleanup_steps=True), dict(yield_host=1)]:
        try: stream('Kernel::Numbers', [1], **options)
        except (ValueError, TypeError): pass
        else: raise AssertionError('Invalid options accepted')
    for name, args in [('Kernel::Closed', []), ('missing', []), ('Kernel::Numbers', [])]:
        try: stream(name, args, **fast)
        except (ValueError, TypeError): pass
        else: raise AssertionError('Invalid export accepted')
    assert i('Factories') == before
    check()
    print(json.dumps(dict(host='python', cases=cases, activeStreams=runtime_info()['activeStreams'])))

asyncio.run(main())
