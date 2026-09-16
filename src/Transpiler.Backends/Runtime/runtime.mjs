// Transpiler portable-mvp semantic helpers. No CLR, eval, opcode decoder, or IL interpreter.
class CliString { constructor(text) { this.text = text; } }
class CliObject { constructor(type) { this.type = type; this.fields = Object.create(null); } }
class CliArray { constructor(element, data) { this.element = element; this.data = data; } }
class CliBox { constructor(type, value) { this.type = type; this.value = value; } }
class CliRef { constructor(get, set) { this.get = get; this.set = set; } }
class CliError extends Error {
    constructor(value) { super(value.type + ': ' + (value.fields.$message ?? '')); this.value = value; this.name = value.type; }
}

class CliFlow {
    constructor(runtime, clauses) {
        this.runtime = runtime; this.clauses = clauses;
        this.pending = []; this.caught = []; this.escaping = null;
    }
    finish(target, error, catcher, stack) {
        stack.length = 0;
        if (catcher !== null) { stack.push(error.value); this.caught.push([catcher, error]); }
        if (target === null) { this.escaping = error; return -1; }
        return target;
    }
    begin(handlers, target, error, catcher, stack) {
        stack.length = 0;
        if (!handlers.length) return this.finish(target, error, catcher, stack);
        this.pending.push({ todo: handlers.slice(1), target, error, catcher, active: handlers[0] });
        return handlers[0].handlerStart;
    }
    leave(pc, target, stack) {
        const handlers = this.clauses.filter(c => c.kind === 'Finally' && c.tryStart <= pc && pc < c.tryEnd &&
            !(c.tryStart <= target && target < c.tryEnd));
        handlers.sort((a, b) => (a.tryEnd - a.tryStart) - (b.tryEnd - b.tryStart));
        return this.begin(handlers, target, null, null, stack);
    }
    handle(error, pc, stack) {
        const candidates = this.clauses.filter(c => c.kind === 'Catch' && c.tryStart <= pc && pc < c.tryEnd && this.runtime.is_type(error.value, c.catchType));
        candidates.sort((a, b) => (a.tryEnd - a.tryStart) - (b.tryEnd - b.tryStart));
        const catcher = candidates[0] ?? null, target = catcher?.handlerStart ?? null;
        this.pending = this.pending.filter(p => target !== null && p.active.handlerStart <= target && target < p.active.handlerEnd);
        const handlers = this.clauses.filter(c => ['Finally', 'Fault'].includes(c.kind) && c.tryStart <= pc && pc < c.tryEnd &&
            (target === null || !(c.tryStart <= target && target < c.tryEnd)));
        handlers.sort((a, b) => (a.tryEnd - a.tryStart) - (b.tryEnd - b.tryStart));
        return this.begin(handlers, target, error, catcher, stack);
    }
    resume(stack) {
        if (!this.pending.length) throw new Error('endfinally without a pending continuation');
        const p = this.pending.at(-1); stack.length = 0;
        if (p.todo.length) { p.active = p.todo.shift(); return p.active.handlerStart; }
        this.pending.pop();
        return this.finish(p.target, p.error, p.catcher, stack);
    }
    rethrow(pc) {
        for (let i = this.caught.length - 1; i >= 0; --i) {
            const [c, e] = this.caught[i];
            if (c.handlerStart <= pc && pc < c.handlerEnd) throw e;
        }
        throw new Error('rethrow outside a catch handler');
    }
}

class CliRuntime {
    constructor(metadata, write = null) {
        this.meta = metadata; this.functions = Object.create(null);
        this.statics = Object.create(null); this.initialization = Object.create(null); this.interned = new Map();
        this.write = write ?? (text => {
            if (typeof process !== 'undefined' && process.stdout?.write) process.stdout.write(text);
            else console.log(text.endsWith('\n') ? text.slice(0, -1) : text);
        });
        this.parents = { 'System.Object': null, 'System.String': 'System.Object', 'System.Exception': 'System.Object',
            'System.SystemException': 'System.Exception', 'System.ArithmeticException': 'System.SystemException' };
        for (const t of ['DivideByZeroException', 'OverflowException']) this.parents['System.' + t] = 'System.ArithmeticException';
        for (const t of ['NullReferenceException', 'IndexOutOfRangeException', 'ArrayTypeMismatchException', 'InvalidCastException',
            'ArgumentException', 'InvalidOperationException', 'NotSupportedException', 'TypeInitializationException', 'OutOfMemoryException'])
            this.parents['System.' + t] = 'System.SystemException';
        this.parents['System.ArgumentOutOfRangeException'] = 'System.ArgumentException';
        for (const [key, value] of Object.entries(metadata.types)) this.parents[key] = value.base;
        for (const [key, field] of Object.entries(metadata.fields)) if (field.static) this.statics[key] = this.default(field.type);
    }
    string(text, intern = false) {
        if (text instanceof CliString) return text;
        if (intern) {
            if (!this.interned.has(text)) this.interned.set(text, new CliString(text));
            return this.interned.get(text);
        }
        return new CliString(text);
    }
    bits(value, width, unsigned = false) {
        const b = unsigned ? BigInt.asUintN(width, BigInt(value)) : BigInt.asIntN(width, BigInt(value));
        return width === 64 ? b : Number(b);
    }
    fail(type, message = '') { const obj = new CliObject(type); obj.fields.$message = message; throw new CliError(obj); }
    nonnull(value) {
        if (value === null || value === undefined) this.fail('System.NullReferenceException', 'Object reference not set to an instance of an object.');
        return value;
    }
    raise_value(value) {
        this.nonnull(value);
        if (!this.is_type(value, 'System.Exception')) throw new TypeError('throw requires a managed exception object');
        throw new CliError(value);
    }
    truth(value) { return value != null && ((typeof value === 'number' || typeof value === 'bigint') ? value !== 0 && value !== 0n : true); }
    default(type) {
        if (['System.Int64', 'System.UInt64'].includes(type)) return 0n;
        if (['System.Boolean', 'System.Char', 'System.SByte', 'System.Byte', 'System.Int16', 'System.UInt16',
            'System.Int32', 'System.UInt32', 'System.Single', 'System.Double'].includes(type)) return 0;
        return null;
    }
    coerce(value, type) {
        const widths = { 'System.SByte': [8, false], 'System.Byte': [8, true], 'System.Int16': [16, false],
            'System.UInt16': [16, true], 'System.Char': [16, true], 'System.Int32': [32, false],
            'System.UInt32': [32, true], 'System.Int64': [64, false], 'System.UInt64': [64, true] };
        if (Object.hasOwn(widths, type)) {
            if (widths[type][0] === 64 && typeof value === 'number' && !Number.isSafeInteger(value))
                throw new TypeError('Int64/UInt64 arguments must use BigInt outside the safe Number range');
            return this.bits(value, ...widths[type]);
        }
        if (type === 'System.Boolean') return value ? 1 : 0;
        if (type === 'System.Double') return Number(value);
        if (type === 'System.String' && typeof value === 'string') return this.string(value);
        if (type.endsWith('[]') && Array.isArray(value)) return new CliArray(type.slice(0, -2), value.map(v => this.coerce(v, type.slice(0, -2))));
        return value;
    }
    binary(op, left, right, kind) {
        const operation = op.split('.')[0];
        if (kind === 'f') {
            switch (operation) {
                case 'add': return left + right;
                case 'sub': return left - right;
                case 'mul': return left * right;
                case 'div': return left / right;
                case 'rem': return left % right;
                default: throw new Error('Invalid floating operation: ' + op);
            }
        }
        const width = kind === 'i8' ? 64 : 32, unsigned = op.endsWith('.un');
        const a = BigInt(this.bits(left, width, unsigned)), b = BigInt(this.bits(right, width, unsigned));
        let value;
        switch (operation) {
            case 'add': value = a + b; break;
            case 'sub': value = a - b; break;
            case 'mul': value = a * b; break;
            case 'div': case 'rem':
                if (b === 0n) this.fail('System.DivideByZeroException', 'Attempted to divide by zero.');
                if (!unsigned && a === -(1n << BigInt(width - 1)) && b === -1n) {
                    if (operation === 'div') this.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.');
                    return kind === 'i8' ? 0n : 0;
                }
                value = operation === 'div' ? a / b : a % b; break;
            case 'and': value = a & b; break;
            case 'or': value = a | b; break;
            case 'xor': value = a ^ b; break;
            case 'shl': value = a << (BigInt(right) & BigInt(width - 1)); break;
            case 'shr': value = a >> (BigInt(right) & BigInt(width - 1)); break;
            default: throw new Error('Invalid integer operation: ' + op);
        }
        if (op.includes('.ovf')) {
            const lo = unsigned ? 0n : -(1n << BigInt(width - 1));
            const hi = unsigned ? (1n << BigInt(width)) - 1n : (1n << BigInt(width - 1)) - 1n;
            if (value < lo || value > hi) this.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.');
        }
        return this.bits(value, width);
    }
    unary(op, value, kind) {
        if (kind === 'f') return -value;
        return this.bits(op === 'neg' ? -BigInt(value) : ~BigInt(value), kind === 'i8' ? 64 : 32);
    }
    compare(op, left, right, kind) {
        const name = op.replace('.un', '');
        if (kind === 'o') {
            if (['ceq', 'beq'].includes(name)) return Number(left === right);
            if (name === 'bne') return Number(left !== right);
            if (op === 'cgt.un') return Number(left != null && right == null);
            throw new Error('Unsupported ordered object comparison');
        }
        if (kind === 'i4' || kind === 'i8') {
            const width = kind === 'i8' ? 64 : 32;
            left = this.bits(left, width, op.endsWith('.un')); right = this.bits(right, width, op.endsWith('.un'));
        } else if (Number.isNaN(left) || Number.isNaN(right)) return Number(op.endsWith('.un'));
        switch (name) {
            case 'ceq': case 'beq': return Number(left === right);
            case 'bne': return Number(left !== right);
            case 'cgt': case 'bgt': return Number(left > right);
            case 'clt': case 'blt': return Number(left < right);
            case 'bge': return Number(left >= right);
            case 'ble': return Number(left <= right);
            default: throw new Error('Invalid comparison: ' + op);
        }
    }
    convert(op, value, source) {
        const target = op.replace(/^conv\./, '').replace(/^ovf\./, '').replace(/\.un$/, '');
        const unsignedSource = op.endsWith('.un') || (target === 'u8' && source === 'i4' && !op.includes('.ovf'));
        if (source === 'i4' || source === 'i8') value = this.bits(value, source === 'i8' ? 64 : 32, unsignedSource);
        if (target === 'r8' || target === 'r') return Number(value);
        const width = { '1': 8, '2': 16, '4': 32, '8': 64 }[target.at(-1)], unsigned = target[0] === 'u';
        if (typeof value === 'number') {
            if (!Number.isFinite(value)) {
                if (op.includes('.ovf')) this.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.');
                throw new Error('Unspecified CLI floating-to-integer conversion is outside portable-mvp');
            }
            value = BigInt(Math.trunc(value));
        }
        if (op.includes('.ovf')) {
            const lo = unsigned ? 0n : -(1n << BigInt(width - 1));
            const hi = unsigned ? (1n << BigInt(width)) - 1n : (1n << BigInt(width - 1)) - 1n;
            if (value < lo || value > hi) this.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.');
        }
        return this.bits(value, width, unsigned);
    }
    is_type(value, target) {
        if (value == null) return false;
        if (target === 'System.Object') return true;
        if (value instanceof CliString) return target === 'System.String';
        if (value instanceof CliBox) return target === value.type || target === 'System.ValueType';
        if (value instanceof CliArray) {
            if (target === 'System.Array') return true;
            if (!target.endsWith('[]')) return false;
            const want = target.slice(0, -2);
            if (value.element === want) return true;
            if (this.default(value.element) !== null || this.default(want) !== null) return false;
            return this.assignable(value.element, want);
        }
        return value instanceof CliObject && this.assignable(value.type, target);
    }
    assignable(actual, target) {
        const seen = new Set();
        while (actual != null && !seen.has(actual)) {
            if (actual === target) return true;
            seen.add(actual); actual = this.parents[actual];
        }
        return false;
    }
    cast(value, target, test = false) {
        if (value === null || this.is_type(value, target)) return value;
        if (test) return null;
        this.fail('System.InvalidCastException', 'Specified cast is not valid.');
    }
    box(value, type) { return this.default(type) !== null ? new CliBox(type, this.coerce(value, type)) : value; }
    unbox(value, type, address = false) {
        if (this.default(type) === null && !address) return this.cast(value, type);
        this.nonnull(value);
        if (!(value instanceof CliBox) || value.type !== type) this.fail('System.InvalidCastException', 'Specified cast is not valid.');
        return address ? new CliRef(() => value.value, v => { value.value = this.coerce(v, type); }) : value.value;
    }
    cell_ref(cells, key, type) { return new CliRef(() => cells[key], v => { cells[key] = this.coerce(v, type); }); }
    load_ref(reference, type) { return this.coerce(this.nonnull(reference).get(), type); }
    store_ref(reference, value, type) { this.nonnull(reference).set(this.coerce(value, type)); }
    array(type, length) {
        if (!Number.isInteger(length) || length < 0 || length > 2147483647) this.fail('System.OverflowException', 'Array dimensions exceeded supported range.');
        try { return new CliArray(type, Array.from({ length }, () => this.default(type))); }
        catch (e) { if (e instanceof RangeError) this.fail('System.OutOfMemoryException', 'Insufficient memory.'); throw e; }
    }
    index(array, index) {
        this.nonnull(array);
        if (index < 0 || index >= array.data.length) this.fail('System.IndexOutOfRangeException', 'Index was outside the bounds of the array.');
        return index;
    }
    array_length(array) { return this.nonnull(array).data.length; }
    array_get(array, index, type) { this.index(array, index); return this.coerce(array.data[index], type); }
    array_set(array, index, value, type) {
        this.index(array, index);
        if (this.default(array.element) === null && value !== null && !this.is_type(value, array.element))
            this.fail('System.ArrayTypeMismatchException', 'Attempted to access an element as a type incompatible with the array.');
        array.data[index] = this.coerce(value, array.element);
    }
    array_ref(array, index, type) {
        this.index(array, index);
        if (array.element !== type) this.fail('System.ArrayTypeMismatchException', 'Array element reference type mismatch.');
        return this.cell_ref(array.data, index, type);
    }
    ensure(type) {
        const t = this.meta.types[type];
        if (!t?.cctor) return;
        const state = this.initialization[type];
        if (state instanceof CliError) throw state;
        if (state !== undefined) return;
        this.initialization[type] = 'running';
        try { this.call(t.cctor, [], false); this.initialization[type] = 'complete'; }
        catch (e) {
            if (!(e instanceof CliError)) throw e;
            const obj = new CliObject('System.TypeInitializationException');
            obj.fields.$message = "The type initializer for '" + type + "' threw an exception.";
            obj.fields.$inner = e.value;
            const wrapped = new CliError(obj); this.initialization[type] = wrapped; throw wrapped;
        }
    }
    field_get(obj, key) {
        const f = this.meta.fields[key];
        if (f.static) { this.ensure(f.owner); return this.statics[key]; }
        return this.nonnull(obj).fields[key];
    }
    field_set(obj, key, value) {
        const f = this.meta.fields[key];
        if (f.static) { this.ensure(f.owner); this.statics[key] = this.coerce(value, f.type); }
        else this.nonnull(obj).fields[key] = this.coerce(value, f.type);
    }
    field_ref(obj, key) {
        const f = this.meta.fields[key];
        if (f.static) this.ensure(f.owner); else this.nonnull(obj);
        return new CliRef(() => this.field_get(obj, key), v => this.field_set(obj, key, v));
    }
    new_object(methodId, args) {
        const method = this.meta.methods[methodId], name = method.type, type = this.meta.types[name];
        if (type && !type.before) this.ensure(name);
        const obj = new CliObject(name), seen = new Set();
        let current = name;
        while (this.meta.types[current] && !seen.has(current)) {
            seen.add(current); const t = this.meta.types[current];
            for (const key of t.fields) { const f = this.meta.fields[key]; if (!f.static) obj.fields[key] = this.default(f.type); }
            current = t.base;
        }
        this.call(methodId, [obj, ...args], false);
        return obj;
    }
    call(methodId, args, virtual = false) {
        let method = this.meta.methods[methodId];
        if (virtual) {
            const receiver = this.nonnull(args[0]);
            if (method.virtual && receiver instanceof CliObject) {
                methodId = this.meta.types[receiver.type]?.vtable?.[method.slot] ?? methodId;
                method = this.meta.methods[methodId];
            }
        }
        const type = this.meta.types[method.type];
        if (type && !type.before && method.name !== '.cctor' && (!method.instance || method.name === '.ctor')) this.ensure(method.type);
        if (method.intrinsic !== null) return this.external(method, args);
        return this.functions[methodId](args);
    }
    double_text(value) {
        if (Number.isNaN(value)) return 'NaN';
        if (!Number.isFinite(value)) return value < 0 ? '-Infinity' : 'Infinity';
        if (value === 0) return Object.is(value, -0) ? '-0' : '0';
        const sign = value < 0 ? '-' : '', text = Math.abs(value).toString().toLowerCase();
        const [mantissa, exponent = '0'] = text.split('e'), [whole, fraction = ''] = mantissa.split('.');
        let exp = Number(exponent) + whole.length - 1;
        if (whole === '0') exp = Number(exponent) - (fraction.length - fraction.replace(/^0+/, '').length) - 1;
        const digits = (whole + fraction).replace(/^0+/, '').replace(/0+$/, '') || '0';
        if (exp < -4 || exp >= 17) return sign + digits[0] + (digits.length > 1 ? '.' + digits.slice(1) : '') +
            'E' + (exp >= 0 ? '+' : '-') + String(Math.abs(exp)).padStart(2, '0');
        const pos = exp + 1;
        if (pos <= 0) return sign + '0.' + '0'.repeat(-pos) + digits;
        if (pos >= digits.length) return sign + digits + '0'.repeat(pos - digits.length);
        return sign + digits.slice(0, pos) + '.' + digits.slice(pos);
    }
    format(value, type = 'System.Object') {
        if (value instanceof CliRef) value = value.get();
        if (value === null) return '';
        if (value instanceof CliString) return value.text;
        if (value instanceof CliBox) return this.format(value.value, value.type);
        if (type === 'System.Boolean') return value ? 'True' : 'False';
        if (type === 'System.Char') return String.fromCharCode(Number(value) & 65535);
        if (type === 'System.Double') return this.double_text(value);
        if (value instanceof CliObject) return value.type;
        return String(this.coerce(value, type));
    }
    external(method, args) {
        const op = method.intrinsic, parameters = method.params;
        if (op.startsWith('console.')) {
            this.write((args.length ? this.format(args[0], parameters[0]) : '') + (op === 'console.line' ? '\n' : '')); return null;
        }
        if (op === 'object.ctor') return null;
        if (op === 'object.string' || op === 'primitive.string') return this.string(this.format(args[0], method.type));
        if (op === 'reference.equals') return Number(args[0] === args[1]);
        if (op.startsWith('string.')) {
            if (op === 'string.equals' || op === 'string.not-equals') {
                const equal = args[0] === null && args[1] === null || args[0] !== null && args[1] !== null && args[0].text === args[1].text;
                return Number(op === 'string.not-equals' ? !equal : equal);
            }
            if (op === 'string.concat') {
                const converted = args.filter(a => a !== null).map(a => a instanceof CliString ? a : this.string(this.format(a)));
                const nonempty = converted.filter(a => a.text.length !== 0);
                return nonempty.length === 1 ? nonempty[0] : this.string(converted.map(a => a.text).join(''), nonempty.length === 0);
            }
            const text = this.nonnull(args[0]).text, length = text.length;
            if (op === 'string.length') return length;
            const start = args[1];
            if (op === 'string.char') {
                if (start < 0 || start >= length) this.fail('System.IndexOutOfRangeException', 'Index was outside the bounds of the array.');
                return text.charCodeAt(start);
            }
            const count = args.length === 2 ? length - start : args[2];
            if (start < 0 || count < 0 || start > length - count) this.fail('System.ArgumentOutOfRangeException', 'Substring range is out of bounds.');
            return this.string(text.slice(start, start + count));
        }
        if (op === 'exception.ctor') {
            args[0].fields.$message = args.length > 1 && args[1] !== null ? this.format(args[1]) : "Exception of type '" + args[0].type + "' was thrown."; return null;
        }
        if (op === 'exception.message') return this.string(this.nonnull(args[0]).fields.$message ?? '');
        if (op === 'double.isnan') return Number(Number.isNaN(args[0]));
        if (op === 'double.isinf') return Number(args[0] === Infinity || args[0] === -Infinity);
        if (op.startsWith('math.')) {
            const a = this.coerce(args[0], parameters[0]);
            if (op === 'math.abs') {
                if ((parameters[0] === 'System.Int32' && a === -2147483648) || (parameters[0] === 'System.Int64' && a === -9223372036854775808n))
                    this.fail('System.OverflowException', 'Negating the minimum value of a twos complement number is invalid.');
                return typeof a === 'bigint' ? (a < 0n ? -a : a) : Math.abs(a);
            }
            if (op === 'math.min' || op === 'math.max') {
                const b = this.coerce(args[1], parameters[1]);
                if (typeof a === 'bigint') return op === 'math.min' ? (a < b ? a : b) : (a > b ? a : b);
                return op === 'math.min' ? Math.min(a, b) : Math.max(a, b);
            }
            if (op === 'math.sqrt') return Math.sqrt(a);
            if (op === 'math.floor') return Math.floor(a);
            if (op === 'math.ceiling') return Math.ceil(a);
            if (op === 'math.truncate') return Math.trunc(a);
        }
        throw new Error('Unimplemented validated intrinsic: ' + op);
    }
    invoke_export(name, args) {
        const candidates = Object.entries(this.meta.exports).filter(([k]) => k === name || k.split('(')[0] === name);
        if (candidates.length !== 1) throw new Error('Use an unambiguous exported method signature: ' + name);
        const token = candidates[0][1], method = this.meta.methods[token];
        if (args.length !== method.params.length) throw new Error('Incorrect argument count');
        const result = this.call(token, args.map((v, i) => this.coerce(v, method.params[i])), false);
        if (result instanceof CliString) return result.text;
        if (method.returns === 'System.Boolean') return Boolean(result);
        return result;
    }
    main(args) {
        const entry = this.meta.entry;
        if (entry === null) throw new Error('This is a library; use invoke(name, args)');
        const method = this.meta.methods[entry], argv = method.params.length ? [this.coerce(args, 'System.String[]')] : [];
        return this.call(entry, argv, false) ?? 0;
    }
}
