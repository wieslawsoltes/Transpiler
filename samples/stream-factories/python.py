import asyncio
from kernel import invoke, stream, stream_info, runtime_info

async def main():
    # Python early break needs a deterministic close scope.
    async with stream('StreamFactories::Delayed', [3]) as values:
        async for value in values:
            print(value)
            if value == 9007199254740994:
                break

    description = stream_info('StreamFactories::Erased')
    if 'System.Int64' not in description['elements']:
        raise RuntimeError('Int64 stream was not linked')
    async with stream('StreamFactories::Erased', [1], element_type='System.Int64') as values:
        async for value in values:
            print(value)

    print(invoke('StreamFactories::Disposed'))
    info = runtime_info()
    print(info['activeStreams'], info['activeStreamFactories'], info['activeTimers'])

asyncio.run(main())
