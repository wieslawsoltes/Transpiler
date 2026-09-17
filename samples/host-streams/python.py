import asyncio
from kernel import stream, runtime_info


async def main():
    total = 0
    # A scope guarantees deterministic cleanup when async-for exits early.
    async with stream('StreamKernel::Squares', [20]) as values:
        async for value in values:
            total += value
            if value == 16:
                break
    print(total)
    print(runtime_info()['activeStreams'])


asyncio.run(main())
