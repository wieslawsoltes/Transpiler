// Explicit binary32 evaluation/storage and bounded little-endian CLI field data.
class CliDataHandle { constructor(data) { this.data = data; } }
// V8 may canonicalize NaNs stored in optimized numeric arrays. Keep NaN bits in an
// immutable tagged value across CLI stacks/locals; finite numbers stay unboxed.
class CliNaN {
    constructor(bits32 = null, bits64 = null) {
        if (bits64 === null) {
            bits32 = bits32 === null ? -4194304 : bits32 | 0;
            bits64 = BigInt.asIntN(64, (BigInt(bits32 >>> 31) << 63n) | 0x7ff0000000000000n | (BigInt(bits32 & 0x7fffff) << 29n));
        }
        this.bits64 = BigInt.asIntN(64, bits64);
        this.bits32 = bits32 === null ? Number(BigInt.asIntN(32, ((BigInt.asUintN(64,bits64) >> 32n) & 0x80000000n) |
            0x7fc00000n | ((bits64 >> 29n) & 0x3fffffn))) : bits32 | 0;
        this.negative = this.bits64 < 0n;
        Object.freeze(this);
    }
    valueOf() { const v = new DataView(new ArrayBuffer(8)); v.setBigInt64(0,this.bits64,true); return v.getFloat64(0,true); }
}
class NumericRuntime extends ValueRuntime {
    nan(negative) { return new CliNaN(negative ? -4194304 : 2143289344); }
    wrap_float(value) {
        if (value instanceof CliNaN || !Number.isNaN(value)) return value;
        const v = new DataView(new ArrayBuffer(8)); v.setFloat64(0,value,true);
        return new CliNaN(null,v.getBigInt64(0,true));
    }
    primitive_equal(a,b) { return super.primitive_equal(a instanceof CliNaN ? Number(a) : a, b instanceof CliNaN ? Number(b) : b); }
    semantic_compare(a,b,type) {
        if (a instanceof CliNaN || b instanceof CliNaN) return super.semantic_compare(Number(a),Number(b),type);
        if (a instanceof CliBox && a.value instanceof CliNaN || b instanceof CliBox && b.value instanceof CliNaN) {
            if (!(a instanceof CliBox) || !(b instanceof CliBox) || a.type !== b.type) this.fail('System.ArgumentException','Incompatible comparison types.');
            return this.semantic_compare(a.value,b.value,a.type);
        }
        return super.semantic_compare(a,b,type);
    }
    compare(op,a,b,kind) { return super.compare(op, a instanceof CliNaN ? Number(a) : a, b instanceof CliNaN ? Number(b) : b, kind); }
    invoke_export(name,args) { const value = super.invoke_export(name,args); return value instanceof CliNaN ? Number(value) : value; }
    async await_export(name,args,options={}) { const value = await super.await_export(name,args,options); return value instanceof CliNaN ? Number(value) : value; }
    coerce(value, type) {
        if (type === 'System.Single' || type === 'System.Double') {
            if (value instanceof CliNaN) return type === 'System.Single' ? new CliNaN(value.bits32) : value;
            return this.wrap_float(type === 'System.Single' ? Math.fround(value) : Number(value));
        }
        return super.coerce(value,type);
    }
    binary(op, a, b, kind) {
        if (kind === 'f' || kind === 'f4') {
            const value = super.binary(op,Number(a),Number(b),'f');
            return this.wrap_float(kind === 'f4' ? Math.fround(value) : value);
        }
        return super.binary(op,a,b,kind);
    }
    unary(op, value, kind) {
        if (value instanceof CliNaN && op === 'neg') return new CliNaN(value.bits32 ^ -2147483648, value.bits64 ^ (-9223372036854775808n));
        return this.wrap_float(kind === 'f4' ? Math.fround(super.unary(op, Number(value), 'f')) : super.unary(op, value, kind));
    }
    convert(op, value, source) {
        if (value instanceof CliNaN && (op === 'conv.r4' || op === 'conv.r8' || op === 'conv.r.un')) return op === 'conv.r4' ? new CliNaN(value.bits32) : value;
        if (op === 'conv.r4' || op === 'conv.r4.from-unsigned') {
            if (source === 'i4' || source === 'i8') {
                const integer=BigInt(this.bits(value,source==='i4'?32:64,op.endsWith('unsigned')));
                const negative=integer<0n, magnitude=negative?-integer:integer;
                const shift=Math.max(0,magnitude.toString(2).length-24);
                let rounded=magnitude>>BigInt(shift);
                if(shift) {
                    const remainder=magnitude-(rounded<<BigInt(shift)), halfway=1n<<BigInt(shift-1);
                    if(remainder>halfway||(remainder===halfway&&(rounded&1n)!==0n)) rounded++;
                }
                const result=Number(rounded)*2**shift;
                return negative?-result:result;
            }
            return this.wrap_float(Math.fround(value));
        }
        return this.wrap_float(super.convert(op, value instanceof CliNaN ? Number(value) : value, source === 'f4' ? 'f' : source));
    }
    finite(value) {
        if (!Number.isFinite(Number(value))) this.fail('System.ArithmeticException', 'Non-finite floating-point value.');
        return value;
    }
    semantic_hash(value, type, dispatch = true) {
        value = this.dereference(value);
        if (value instanceof CliNaN) return type === 'System.Single' ? 2139095040 : 2146435072;
        if (type === 'System.Single' && !(value instanceof CliBox)) {
            if (value === 0) return 0;
            if (Number.isNaN(value)) return 2139095040;
            const view = new DataView(new ArrayBuffer(4)); view.setFloat32(0, value, true); return view.getInt32(0, true);
        }
        return super.semantic_hash(value, type, dispatch);
    }
    format(value, type = 'System.Object') {
        value = this.dereference(value);
        if (value instanceof CliBox) return this.format(value.value, value.type);
        if (value instanceof CliNaN) return 'NaN';
        if (type !== 'System.Single') return super.format(value, type);
        value = Math.fround(value);
        if (!Number.isFinite(value) || value === 0) return this.double_text(value);
        let text;
        for (let precision = 1; precision <= 9; precision++) {
            text = value.toPrecision(precision);
            if (Math.fround(Number(text)) === value) break;
        }
        const sign = text.startsWith('-') ? '-' : '';
        const [mantissa,power='0'] = text.replace(/^-/, '').toLowerCase().split('e');
        const [whole,fraction=''] = mantissa.split('.');
        let exponent = Number(power) + whole.length - 1;
        if (whole === '0') exponent = Number(power) - (fraction.length - fraction.replace(/^0+/, '').length) - 1;
        const digits = (whole+fraction).replace(/^0+/, '').replace(/0+$/, '') || '0';
        if (exponent < -4 || exponent >= 9) return sign + digits[0] + (digits.length>1 ? '.'+digits.slice(1) : '') + 'E' + (exponent>=0 ? '+' : '-') + String(Math.abs(exponent)).padStart(2,'0');
        const position = exponent+1;
        if(position<=0) return sign+'0.'+'0'.repeat(-position)+digits;
        if(position>=digits.length) return sign+digits+'0'.repeat(position-digits.length);
        return sign+digits.slice(0,position)+'.'+digits.slice(position);
    }
    data_handle(key) {
        const data = this.meta.fields[key].data;
        if (data === null) throw new Error('Missing validated field data');
        return new CliDataHandle(new Uint8Array(data));
    }
    initialize_data(array, handle) {
        if (array === null) this.fail('System.ArgumentNullException','Array cannot be null.');
        if (!(array instanceof CliArray) || !(handle instanceof CliDataHandle)) this.fail('System.ArgumentException','Invalid array initialization arguments.');
        const element = this.meta.types[array.element]?.enumType ?? array.element;
        const formats = {'System.Boolean':[1,'getUint8'],'System.SByte':[1,'getInt8'],'System.Byte':[1,'getUint8'],
            'System.Char':[2,'getUint16'],'System.Int16':[2,'getInt16'],'System.UInt16':[2,'getUint16'],
            'System.Int32':[4,'getInt32'],'System.UInt32':[4,'getUint32'],'System.Int64':[8,'getBigInt64'],
            'System.UInt64':[8,'getBigUint64'],'System.Single':[4,'getFloat32'],'System.Double':[8,'getFloat64']};
        const format = formats[element];
        if (!format) this.fail('System.ArgumentException','Array initialization requires primitive or enum elements.');
        const [size,reader] = format;
        if (array.data.length > Math.floor(handle.data.length/size)) this.fail('System.ArgumentException','Field data is shorter than the array.');
        const view = new DataView(handle.data.buffer,handle.data.byteOffset,handle.data.byteLength);
        for(let index=0; index<array.data.length; index++) {
            let value = view[reader](index*size,true);
            if (Number.isNaN(value)) value = size === 4 ? new CliNaN(view.getInt32(index*size,true)) : new CliNaN(null,view.getBigInt64(index*size,true));
            array.data[index] = this.coerce(value,element);
        }
    }
    external(method,args) {
        const op = method.intrinsic;
        if(op === 'array.initialize-data') { this.initialize_data(args[0],args[1]); return null; }
        if(op.startsWith('bits.')) {
            const view=new DataView(new ArrayBuffer(8));
            switch(op) {
                case 'bits.single-i4': if(args[0] instanceof CliNaN)return args[0].bits32; view.setFloat32(0,args[0],true); return view.getInt32(0,true);
                case 'bits.i4-single': view.setInt32(0,args[0],true); return Number.isNaN(view.getFloat32(0,true)) ? new CliNaN(view.getInt32(0,true)) : view.getFloat32(0,true);
                case 'bits.double-i8': if(args[0] instanceof CliNaN)return args[0].bits64; view.setFloat64(0,args[0],true); return view.getBigInt64(0,true);
                case 'bits.i8-double': view.setBigInt64(0,args[0],true); return Number.isNaN(view.getFloat64(0,true)) ? new CliNaN(null,args[0]) : view.getFloat64(0,true);
                default: throw new Error('Unknown validated bit conversion');
            }
        }
        if(op.startsWith('float.')) {
            const value=Number(args[0]);
            switch(op.slice(6)) {
                case 'IsFinite': return Number(Number.isFinite(value));
                case 'IsNaN': return Number(Number.isNaN(value));
                case 'IsInfinity': return Number(value===Infinity||value===-Infinity);
                case 'IsPositiveInfinity': return Number(value===Infinity);
                case 'IsNegativeInfinity': return Number(value===-Infinity);
                case 'IsNegative': { if(args[0] instanceof CliNaN)return Number(args[0].negative); const v=new DataView(new ArrayBuffer(8));v.setFloat64(0,value,true);return v.getUint32(4,true)>>>31; }
            }
        }
        if(op.startsWith('mathf.') || op.startsWith('math.')) {
            const single = op.startsWith('mathf.'), name=op.slice(single?6:5);
            let value;
            if(name==='copysign') {
                const sign = new DataView(new ArrayBuffer(8)); sign.setFloat64(0,Number(args[1]),true);
                const negative = args[1] instanceof CliNaN ? args[1].negative : (sign.getUint32(4,true)>>>31)!==0;
                if(args[0] instanceof CliNaN) return new CliNaN((args[0].bits32 & 0x7fffffff) | (negative?-2147483648:0));
                value=negative ? -Math.abs(args[0]) : Math.abs(args[0]);
            } else if(name==='abs' && args[0] instanceof CliNaN) return new CliNaN(args[0].bits32 & 0x7fffffff, args[0].bits64 & 0x7fffffffffffffffn);
            else if((name==='min'||name==='max') && args.some(a=>a instanceof CliNaN)) return args.find(a=>a instanceof CliNaN);
            else value=super.external(single?{...method,intrinsic:'math.'+name,params:args.map(_=>'System.Double')}:method,args.map(a=>a instanceof CliNaN?Number(a):a));
            return this.wrap_float(single?Math.fround(value):value);
        }
        return super.external(method,args);
    }
}
