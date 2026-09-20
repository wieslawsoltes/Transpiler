import asyncio
import json
from library import invoke, stream, stream_info, runtime_info, StreamCleanupPendingError


def name(n): return 'StreamExports::' + n
def call(n, args=()): return invoke(name(n), args)
async def yield_now(): pass
fast = dict(max_steps=64, cleanup_steps=64, yield_host=yield_now)
async def values(s): return [value async for value in s]

def clean():
    info = runtime_info()
    for key in ('activeStreams', 'activeStreamFactories', 'activeTimers', 'hostWaiters'):
        assert info[key] == 0, (key, info)

async def until(predicate):
    for _ in range(200):
        if predicate(): return
        await asyncio.sleep(0.001)
    raise AssertionError('Factory never reached the expected phase')

def raises(action, text):
    try: action()
    except Exception as error:
        assert text in str(error), str(error)
        return
    raise AssertionError('Expected failure: ' + text)

async def rejects(operation, predicate):
    try: await operation
    except BaseException as error:
        assert predicate(error), repr(error)
        return error
    raise AssertionError('Expected operation to fail')

async def main():
    cases = []
    def begin(): clean(); call('Reset')
    def passed(n): clean(); cases.append(n)
    int_type, text_type = 'System.Int32', 'System.String'
    for n in ('Direct', 'Concrete', 'Inherited', 'Interface', 'Struct', 'TaskConcrete', 'TaskStruct', 'ValueConcrete', 'ValueStruct', 'TaskInterface', 'AsyncValue'):
        begin()
        assert stream_info(name(n))['elements'] == (int_type,)
        assert await values(stream(name(n), **fast)) == [10, 11]
        assert call('Factories') == call('Acquisitions') == call('Disposals') == 1
        passed('shape/' + n)
    begin()
    info = stream_info(name('Concrete'))
    assert info['policy'] == 'managed-stream-v2' and info['kind'] == 'value'
    assert stream_info(name('TaskConcrete'))['kind'] == 'task'
    assert stream_info(name('ValueConcrete'))['kind'] == 'value-task'
    info['elements'] = ('System.Double',)
    assert stream_info(name('Concrete'))['elements'] == (int_type,)
    assert call('Factories') == 0
    unused = stream(name('TaskConcrete')); await unused.aclose()
    assert call('Factories') == 0
    passed('metadata-no-acquisition')
    for n, args in (('Erased', [0]), ('Erased', [1]), ('TaskErased', []), ('ValueErased', [])):
        begin()
        assert stream_info(name(n))['requiresElement']
        raises(lambda: stream(name(n), args), 'element_type')
        assert call('Factories') == 0
        assert await values(stream(name(n), args, **fast, element_type=int_type)) == [10, 11]
        passed('erased/' + n + ''.join(map(str, args)))
    for n in ('Multiple', 'TaskMultiple'):
        begin()
        assert stream_info(name(n))['elements'] == (int_type, text_type)
        raises(lambda: stream(name(n)), 'element_type')
        assert call('Factories') == 0
        assert await values(stream(name(n), **fast, element_type=int_type)) == [10, 11]
        assert await values(stream(name(n), **fast, element_type=text_type)) == ['alpha', 'beta']
        passed('ambiguous/' + n)
    begin()
    for n in ('NonStream', 'NestedUnsupported'): raises(lambda n=n: stream(name(n)), 'no linked stream')
    raises(lambda: stream(name('Concrete'), element_type='System.Double'), 'not linked')
    raises(lambda: stream(name('Concrete'), element_type=3), 'element_type')
    raises(lambda: stream(name('Concrete'), [1]), 'argument count')
    raises(lambda: stream(name('Overloaded')), 'unambiguous')
    assert call('Factories') == 0
    passed('rejections-before-user-code')
    begin()
    assert await values(stream(name('Overloaded(System.Int32)'), [20], **fast)) == [20, 21]
    passed('overload-exact-signature')
    for n, args, exception in (
        ('ThrowingFactory', [], 'InvalidOperationException'), ('FaultedFactory', [], 'InvalidOperationException'),
        ('CanceledFactory', [], 'TaskCanceledException'), ('NullDirect', [], 'ArgumentNullException'),
        ('NullTask', [], 'ArgumentNullException'), ('NullResult', [], 'ArgumentNullException'),
        ('DefaultValue', [], 'ArgumentNullException'), ('BadAcquisition', [], 'InvalidOperationException'),
        ('NullEnumerator', [], 'InvalidOperationException'), ('Erased', [2], 'InvalidCastException'),
        ('Erased', [3], 'ArgumentNullException')):
        begin()
        s = stream(name(n), args, **fast, **(dict(element_type=int_type) if n == 'Erased' else {}))
        await rejects(anext(s), lambda error: error.value.type.endswith(exception))
        assert s.closed
        await s.aclose()
        assert call('Factories') == 1
        passed('failure/' + n + ''.join(map(str, args)))
    begin()
    s = stream(name('Erased'), [0], **fast, element_type=text_type)
    await rejects(anext(s), lambda e: e.value.type.endswith('InvalidCastException'))
    assert call('Acquisitions') == 0
    passed('checked-erased-interface-cast')
    begin()
    assert await values(stream(name('CompletedSource'), **fast)) == [10, 11]
    assert call('Consumptions') == 1
    passed('source-completed-consumed-once')
    begin()
    s = stream(name('SourceFactory'), max_steps=2, cleanup_steps=2, yield_host=yield_now)
    for operation in (lambda: anext(s), s.aclose):
        await rejects(operation(), lambda e: isinstance(e, StreamCleanupPendingError) and e.phase == 'factory')
        assert s.pending == 'factory' and not s.closed
        assert runtime_info()['activeStreamFactories'] == 1 and call('Consumptions') == 0
    call('CompleteSource'); await s.aclose()
    assert call('Factories') == call('Consumptions') == 1
    assert call('AcquiredCanceled') and call('Moves') == 0 and call('Disposals') == 1
    passed('factory-cleanup-timeout-and-retry')
    begin()
    s = stream(name('PendingFactory'), max_steps=1, cleanup_steps=1, yield_host=yield_now)
    await rejects(anext(s), lambda e: e.phase == 'factory')
    call('FailFactory'); await rejects(s.aclose(), lambda e: 'factory-failure' in str(e))
    assert s.closed and call('Acquisitions') == 0
    passed('pending-factory-fault-retires-on-close')
    begin()
    s = stream(name('DelayedFactory')); move = asyncio.create_task(anext(s))
    await until(lambda: s.pending == 'factory' and runtime_info()['hostWaiters'] == 1)
    await s.aclose()
    try: await move
    except StopAsyncIteration: pass
    assert call('Factories') == call('Acquisitions') == 1
    assert call('AcquiredCanceled') and call('Moves') == 0 and call('Disposals') == 1
    passed('close-while-acquiring-without-move')
    begin()
    s = stream(name('DelayedFactory')); move = asyncio.create_task(anext(s))
    await until(lambda: s.pending == 'factory' and runtime_info()['hostWaiters'] == 1)
    move.cancel(); await rejects(move, lambda e: isinstance(e, asyncio.CancelledError))
    assert call('AcquiredCanceled') and call('Moves') == 0 and call('Disposals') == 1
    passed('abort-during-factory-drains-ownership')
    begin()
    s = stream(name('TaskConcrete')); s.cancel()
    await rejects(anext(s), lambda e: 'canceled' in str(e))
    assert call('Factories') == 0
    passed('pre-canceled-factory-not-invoked')
    begin()
    s = stream(name('DelayedDispose')); assert await anext(s) == 10
    await s.aclose(); assert call('Disposals') == 1
    passed('acquisition-and-disposal-delays')
    begin()
    s = stream(name('FaultingDispose'), **fast); await anext(s)
    await rejects(s.aclose(), lambda e: 'dispose-failure' in str(e))
    assert s.closed and call('Disposals') == 1
    await s.aclose()
    passed('disposal-fault-retires')
    begin()
    supplied = False
    async def supply():
        nonlocal supplied
        if not supplied: supplied = True; call('CompleteSource')
    s = stream(name('SourceFactory'), max_steps=64, cleanup_steps=64, yield_host=supply)
    assert await values(s) == [10, 11] and call('Consumptions') == 1
    passed('source-completed-by-custom-yield')
    begin()
    s = stream(name('DelayedFactory')); first = asyncio.create_task(anext(s))
    await until(lambda: s.pending == 'factory')
    await rejects(anext(s), lambda e: 'Overlapping' in str(e))
    assert await first == 10
    await s.aclose(); assert call('Factories') == 1
    passed('overlapping-factory-next-rejected')
    begin()
    a, b = stream(name('DelayedFactory')), stream(name('DelayedFactory'))
    assert await asyncio.gather(values(a), values(b)) == [[10, 11], [10, 11]]
    assert call('Factories') == call('Disposals') == 2
    passed('two-independent-factories')
    begin()
    result = await values(stream(name('ValueElements'), **fast))
    def field(v, n): return next(value for key, value in v.fields.items() if key.endswith('::' + n))
    assert field(result[0], 'X') == 9007199254740993
    assert field(result[1], 'X') == 9007199254740995 and field(result[0], 'Y') == 1
    passed('structured-elements-preserve-int64')
    begin()
    for _ in range(40):
        s = stream(name('TaskConcrete'), **fast); assert await anext(s) == 10; await s.aclose()
    assert call('Factories') == call('Disposals') == 40
    passed('retained-elements-and-factory-stress')
    print(json.dumps(dict(cases=len(cases), names=cases, info=runtime_info())))

asyncio.run(main())
