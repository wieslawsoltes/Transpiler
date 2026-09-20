import asyncio
from library import invoke, stream
async def consume(n, **options):
    async for value in stream('StreamExports::' + n, **options): print(value)
async def main():
    for n in ('Concrete', 'Struct', 'Interface', 'TaskConcrete', 'TaskStruct', 'AsyncValue'): await consume(n)
    await consume('TaskErased', element_type='System.Int32')
    await consume('CompletedSource')
    await consume('Covariant')
    async for pair in stream('StreamExports::ValueElements'):
        for n in ('X', 'Y'): print(next(v for k,v in pair.fields.items() if k.endswith('::' + n)))
    print(invoke('StreamExports::Consumptions'))
    print(invoke('StreamExports::Disposals'))
asyncio.run(main())
