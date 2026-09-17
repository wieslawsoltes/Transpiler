import asyncio
from library import stream
async def main():
 async for value in stream('Kernel::Numbers',[3]):print(value)
 async for value in stream('Kernel::Text'):print('' if value is None else value)
 async for value in stream('Kernel::Flags'):print(value)
asyncio.run(main())
