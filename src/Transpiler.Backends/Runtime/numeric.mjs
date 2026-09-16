// Explicit binary32 evaluation/storage and bounded little-endian CLI field data.
class CliDataHandle { constructor(data) { this.data = data; } }
class NumericRuntime extends ValueRuntime {
    coerce(value, type) { return type === 'System.Single' ? Math.fround(value) : super.coerce(value, type); }
    binary(op, a, b, kind) { return kind === 'f4' ? Math.fround(super.binary(op, a, b, 'f')) : super.binary(op, a, b, kind); }
    unary(op, value, kind) { return kind === 'f4' ? Math.fround(super.unary(op, value, 'f')) : super.unary(op, value, kind); }
    convert(op, value, source) {
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
            return Math.fround(value);
        }
        return super.convert(op, value, source === 'f4' ? 'f' : source);
    }
    finite(value) {
        if (!Number.isFinite(value)) this.fail('System.ArithmeticException', 'Non-finite floating-point value.');
        return value;
    }
    semantic_hash(value, type, dispatch = true) {
        value = this.dereference(value);
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
        this.nonnull(array);
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
        for(let index=0; index<array.data.length; index++) array.data[index] = this.coerce(view[reader](index*size,true),element);
    }
    external(method,args) {
        const op = method.intrinsic;
        if(op === 'array.initialize-data') { this.initialize_data(args[0],args[1]); return null; }
        if(op.startsWith('bits.')) {
            const view=new DataView(new ArrayBuffer(8));
            switch(op) {
                case 'bits.single-i4': view.setFloat32(0,args[0],true); return view.getInt32(0,true);
                case 'bits.i4-single': view.setInt32(0,args[0],true); return view.getFloat32(0,true);
                case 'bits.double-i8': view.setFloat64(0,args[0],true); return view.getBigInt64(0,true);
                case 'bits.i8-double': view.setBigInt64(0,args[0],true); return view.getFloat64(0,true);
                default: throw new Error('Unknown validated bit conversion');
            }
        }
        if(op.startsWith('float.')) {
            const value=args[0];
            switch(op.slice(6)) {
                case 'IsFinite': return Number(Number.isFinite(value));
                case 'IsNaN': return Number(Number.isNaN(value));
                case 'IsInfinity': return Number(value===Infinity||value===-Infinity);
                case 'IsPositiveInfinity': return Number(value===Infinity);
                case 'IsNegativeInfinity': return Number(value===-Infinity);
                case 'IsNegative': { const v=new DataView(new ArrayBuffer(8));v.setFloat64(0,value,true);return v.getUint32(4,true)>>>31; }
            }
        }
        if(op.startsWith('mathf.')) {
            const name=op.slice(6);
            if(name==='copysign') return Math.fround(args[1]<0||Object.is(args[1],-0) ? -Math.abs(args[0]) : Math.abs(args[0]));
            return Math.fround(super.external({...method,intrinsic:'math.'+name,params:args.map(_=>'System.Double')},args));
        }
        return super.external(method,args);
    }
}
