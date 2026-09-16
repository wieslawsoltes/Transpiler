// CLI array shapes and opaque type identity; member reflection is a separate capability.
class CliTypeHandle { constructor(name) { this.name = name; } }
class CliType extends CliObject { constructor(name) { super('System.RuntimeType'); this.name = name; } }
class CliRectArray extends CliArray {
    constructor(element, data, lengths, lower) {
        super(element, data); this.lengths = Object.freeze([...lengths]); this.lower = Object.freeze([...lower]);
        this.type = element + '[rank=' + lengths.length + ']';
    }
}
class ArrayRuntime extends ExceptionRuntime {
    constructor(metadata, write = null) { super(metadata, write); this.parents['System.TypeLoadException']='System.SystemException'; this.typeCache = new Map(); }
    shape(name) {
        if (name.endsWith('[]')) return [name.slice(0,-2),1,true];
        const at=name.lastIndexOf('[rank=');
        return at >= 0 && name.endsWith(']') ? [name.slice(0,at),Number(name.slice(at+6,-1)),false] : null;
    }
    type_handle(name) { return new CliTypeHandle(name); }
    type_object(name) { if (!this.typeCache.has(name)) this.typeCache.set(name,new CliType(name)); return this.typeCache.get(name); }
    actual_type(value, fallback='System.Object') { return value instanceof CliRectArray ? value.type : super.actual_type(value,fallback); }
    format(value,type='System.Object') {
        if(value instanceof CliType) return this.type_text(value.name);
        if(value instanceof CliRectArray) return this.type_text(value.type);
        return super.format(value,type);
    }
    type_text(name) {
        const shape=this.shape(name);
        if(shape) return this.type_text(shape[0])+(shape[2]?'[]':shape[1]===1?'[*]':'['+','.repeat(shape[1]-1)+']');
        if(name.startsWith('[')) name=name.slice(name.indexOf(']')+1);
        return name.replaceAll('<','[').replaceAll('>',']');
    }
    is_type(value,target) {
        if(value instanceof CliType) return ['System.Object','System.Type','System.Reflection.MemberInfo','System.RuntimeType'].includes(target);
        if(value instanceof CliRectArray) {
            if(['System.Object','System.Array','System.Collections.IEnumerable','System.ICloneable'].includes(target)) return true;
            const shape=this.shape(target);
            if(!shape || shape[2] || shape[1]!==value.lengths.length) return false;
            return value.element===shape[0] || this.default(value.element)===null && this.default(shape[0])===null && this.assignable(value.element,shape[0]);
        }
        return super.is_type(value,target);
    }
    rect_array(element,lengths,lower=null,vector=false,creation=false) {
        lower=lower===null?lengths.map(()=>0):[...lower];
        if(lengths.length>32)this.fail('System.TypeLoadException','Array rank exceeds 32.');
        if(lengths.length===0 || lower.length!==lengths.length) this.fail('System.ArgumentException','Invalid array rank or lower bounds.');
        for(let i=0;i<lengths.length;i++) {
            if(lengths[i]<0) this.fail(creation?'System.ArgumentOutOfRangeException':'System.OverflowException','Negative array length.');
            if(lower[i]+lengths[i]-1>2147483647) this.fail('System.ArgumentOutOfRangeException','Array upper bound exceeds Int32.');
        }
        const count=lengths.includes(0)?0:lengths.reduce((a,b)=>a*b,1);
        if(count>2147483647) this.fail('System.OutOfMemoryException','Array exceeds the portable storage limit.');
        if(element==='System.Void'||element.endsWith('&')||element.endsWith('*')) this.fail('System.NotSupportedException','Invalid array element type.');
        const array=this.array(element,count);
        return vector&&lengths.length===1&&lower[0]===0?array:new CliRectArray(element,array.data,lengths,lower);
    }
    array_shape(array) { this.nonnull(array); return array instanceof CliRectArray ? [array.lengths,array.lower] : [[array.data.length],[0]]; }
    flat_index(array,indices) {
        const [lengths,lower]=this.array_shape(array);
        if(indices.length!==lengths.length) this.fail('System.ArgumentException','Index count does not match array rank.');
        let index=0;
        for(let i=0;i<indices.length;i++) {
            const value=indices[i];
            if(value < -2147483648 || value > 2147483647) this.fail('System.ArgumentOutOfRangeException','Index exceeds Int32.');
            const relative=Number(value)-lower[i];
            if(relative<0||relative>=lengths[i]) this.fail('System.IndexOutOfRangeException','Array index out of bounds.');
            index=index*lengths[i]+relative;
        }
        return index;
    }
    new_object(methodId,args) {
        const method=this.meta.methods[methodId];
        if(method.intrinsic==='rect.new') {
            const [element,rank]=this.shape(method.type);
            return this.rect_array(element,args.length===rank?args:args.filter((_,i)=>i%2===1),args.length===rank?null:args.filter((_,i)=>i%2===0));
        }
        return super.new_object(methodId,args);
    }
    store_boxed(array,index,value) {
        const element=array.element;
        if(this.default(element)===null) {
            if(value!==null&&!this.is_type(value,element)) this.fail('System.InvalidCastException','Invalid array element type.');
            array.data[index]=value; return;
        }
        if(value===null) {array.data[index]=this.default(element);return;}
        if(this.nullable(element)||value instanceof CliBox&&value.type===element) {array.data[index]=this.unbox(value,element);return;}
        const widening={
            'System.SByte':['System.Int16','System.Int32','System.Int64','System.Single','System.Double'],
            'System.Byte':['System.Char','System.Int16','System.UInt16','System.Int32','System.UInt32','System.Int64','System.UInt64','System.Single','System.Double'],
            'System.Char':['System.UInt16','System.Int32','System.UInt32','System.Int64','System.UInt64','System.Single','System.Double'],
            'System.Int16':['System.Int32','System.Int64','System.Single','System.Double'],
            'System.UInt16':['System.Char','System.Int32','System.UInt32','System.Int64','System.UInt64','System.Single','System.Double'],
            'System.Int32':['System.Int64','System.Single','System.Double'],
            'System.UInt32':['System.Int64','System.UInt64','System.Single','System.Double'],
            'System.Int64':['System.Single','System.Double'],'System.UInt64':['System.Single','System.Double'],'System.Single':['System.Double']};
        if(this.meta.types[element]?.valueType)this.fail('System.InvalidCastException','Array value type requires an exact boxed match.');
        const target=element,source=this.meta.types[value.type]?.enumType??value.type;
        if(!(value instanceof CliBox)||target!==source&&!(widening[source]??[]).includes(target)) {
            const numeric=Object.hasOwn(widening,source)||['System.Boolean','System.Double'].includes(source);
            this.fail(numeric?'System.ArgumentException':'System.InvalidCastException','Invalid array element conversion.');
        }
        let number=value.value;
        if(target==='System.Single'&&['System.Int64','System.UInt64'].includes(source)) number=this.convert(source==='System.UInt64'?'conv.r4.from-unsigned':'conv.r4',number,'i8');
        array.data[index]=this.coerce(number,element);
    }
    external(method,args) {
        const op=method.intrinsic;
        if(op==='type.from-handle') return this.type_object(args[0].name);
        if(op==='type.of-object') return this.type_object(this.actual_type(this.nonnull(args[0])));
        if(op==='type.equals'||op==='type.not-equals') return Number((args[0]===args[1])!==(op==='type.not-equals'));
        if(['type.element','type.rank','type.is-array'].includes(op)) {
            const shape=this.shape(this.nonnull(args[0]).name);
            if(op==='type.element') return shape?this.type_object(shape[0]):null;
            if(op==='type.is-array') return Number(shape!==null);
            if(!shape) this.fail('System.ArgumentException','Type is not an array.');return shape[1];
        }
        if(op?.startsWith('rect.')) {
            const array=this.nonnull(args[0]),indices=op==='rect.set'?args.slice(1,-1):args.slice(1),index=this.flat_index(array,indices);
            if(op==='rect.get') return this.coerce(array.data[index],method.returns);
            if(op==='rect.address') {
                const element=method.returns.slice(0,-1);
                if(element!==array.element) this.fail('System.ArrayTypeMismatchException','Array address type mismatch.');
                return this.cell_ref(array.data,index,element);
            }
            this.array_set(array,index,args.at(-1),array.element);return null;
        }
        if(!op?.startsWith('array.')||op==='array.initialize-data') return super.external(method,args);
        if(op.startsWith('array.create')) {
            if(args[0]===null) this.fail('System.ArgumentNullException','Element type is null.');
            if(!(args[0] instanceof CliType)) this.fail('System.ArgumentException','Invalid element type.');
            if(op!=='array.create'&&args[1]===null||op==='array.create-bounds'&&args[2]===null) this.fail('System.ArgumentNullException','Array lengths or bounds are null.');
            const lengths=op==='array.create'?args.slice(1):args[1].data,lower=op==='array.create-bounds'?args[2].data:null;
            return this.rect_array(args[0].name,lengths,lower,true,true);
        }
        if((op==='array.clear'||op==='array.clear-all')&&args[0]===null)this.fail('System.ArgumentNullException','Array is null.');
        const array=this.nonnull(args[0]),[lengths,lower]=this.array_shape(array);
        if(op==='array.Length') return array.data.length;
        if(op==='array.LongLength') return BigInt(array.data.length);
        if(op==='array.Rank') return lengths.length;
        if(['array.GetLength','array.GetLongLength','array.GetLowerBound','array.GetUpperBound'].includes(op)) {
            const dimension=args[1];
            if(dimension<0||dimension>=lengths.length) this.fail('System.IndexOutOfRangeException','Invalid dimension.');
            if(op==='array.GetLowerBound') return lower[dimension];
            if(op==='array.GetUpperBound') return this.bits(lower[dimension]+lengths[dimension]-1,32);
            return op==='array.GetLongLength'?BigInt(lengths[dimension]):lengths[dimension];
        }
        if(op==='array.clone') {
            const data=array.data.map(v=>this.copy_value(v));
            return array instanceof CliRectArray?new CliRectArray(array.element,data,lengths,lower):new CliArray(array.element,data);
        }
        if(op==='array.clear'||op==='array.clear-all') {
            const start=op==='array.clear'?args[1]-lower[0]:0,count=op==='array.clear'?args[2]:array.data.length;
            if(start<0||count<0||start>array.data.length-count) this.fail('System.IndexOutOfRangeException','Invalid array range.');
            for(let i=start;i<start+count;i++) array.data[i]=this.default(array.element);return null;
        }
        if(op==='array.enumerate') {
            const constructor=this.meta.arrayEnumerators[array.element+'[]'];
            if(!constructor) throw new Error('Array enumerator was not rooted.');
            return this.new_object(constructor,[new CliArray(array.element,array.data)]);
        }
        let values=op==='array.set-value'?args.slice(2):args.slice(1);
        if(method.params.at(-1).endsWith('[]')) {if(values[0]===null)this.fail('System.ArgumentNullException','Indices are null.');values=values[0].data;}
        else if(method.params.at(-1)==='System.Int64'&&values.some(v=>v < -2147483648 || v > 2147483647))this.fail('System.ArgumentOutOfRangeException','Index exceeds Int32.');
        const index=this.flat_index(array,values);
        if(op==='array.get-value') return this.box(array.data[index],array.element);
        this.store_boxed(array,index,args[1]);return null;
    }
}
