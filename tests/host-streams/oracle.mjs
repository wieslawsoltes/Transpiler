import {stream} from './library.mjs';
for await(const value of stream('Kernel::Numbers',[3]))console.log(value.toString());
for await(const value of stream('Kernel::Text'))console.log(value??'');
for await(const value of stream('Kernel::Flags'))console.log(value?'True':'False');
