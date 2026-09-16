// Managed values copy; managed references alias storage. Neither is a plain host object convention.
class CliValue extends CliObject {}
class ManagedRuntime extends CliRuntime {
    constructor(metadata, write = null) {
        super(metadata, write);
        this.parents['System.ArgumentNullException'] = 'System.ArgumentException';
        this.parents['System.OperationCanceledException'] = 'System.SystemException';
        this.parents['System.Threading.Tasks.TaskCanceledException'] = 'System.OperationCanceledException';
    }
    default(type) {
        const descriptor = this.meta.types[type];
        if (descriptor?.enumType) return super.default(descriptor.enumType);
        if (descriptor?.valueType) {
            const value = new CliValue(type);
            for (const key of descriptor.fields) {
                const field = this.meta.fields[key];
                if (!field.static) value.fields[key] = this.default(field.type);
            }
            return value;
        }
        return super.default(type);
    }
    copy_value(value) {
        if (!(value instanceof CliValue)) return value;
        const copy = new CliValue(value.type);
        for (const [key, item] of Object.entries(value.fields)) copy.fields[key] = this.copy_value(item);
        return copy;
    }
    coerce(value, type) {
        const descriptor = this.meta.types[type];
        if (descriptor?.enumType) return super.coerce(value, descriptor.enumType);
        if (descriptor?.valueType) {
            if (!(value instanceof CliValue) || value.type !== type) throw new TypeError('Invalid managed value representation for ' + type);
            return this.copy_value(value);
        }
        return super.coerce(value, type);
    }
    dereference(value) { return value instanceof CliRef ? value.get() : value; }
    format(value, type = 'System.Object') {
        value = this.dereference(value);
        if (value instanceof CliBox) return this.format(value.value, value.type);
        const descriptor = this.meta.types[type];
        if (descriptor?.enumType) return this.enum_text(value, descriptor);
        if (value instanceof CliObject) {
            let name = value.type;
            if (name.startsWith('[')) name = name.slice(name.indexOf(']') + 1);
            return name.replaceAll('<', '[').replaceAll('>', ']');
        }
        return super.format(value, type);
    }
    enum_text(value, descriptor) {
        const width = ['System.Int64', 'System.UInt64'].includes(descriptor.enumType) ? 64 : 32;
        const bits = BigInt.asUintN(width, BigInt(value));
        const entries = descriptor.enumValues.map(item => [BigInt.asUintN(width, BigInt(item.value)), item.name]);
        entries.sort((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);
        for (const [number, name] of entries) if (number === bits) return name;
        if (descriptor.enumFlags && bits !== 0n) {
            let remaining = bits; const names = [];
            for (let i = entries.length - 1; i >= 0; --i) {
                const [number, name] = entries[i];
                if (number !== 0n && (remaining & number) === number) { remaining &= ~number; names.unshift(name); }
            }
            if (remaining === 0n) return names.join(', ');
        }
        return String(value);
    }
    field_get(obj, key) { return this.coerce(super.field_get(this.dereference(obj), key), this.meta.fields[key].type); }
    field_set(obj, key, value) { super.field_set(this.dereference(obj), key, value); }
    field_ref(obj, key) {
        const field = this.meta.fields[key];
        if (field.static) { this.ensure(field.owner); return this.cell_ref(this.statics, key, field.type); }
        this.nonnull(this.dereference(obj));
        // Keep the location, not a stale object captured before its enclosing struct is replaced.
        return new CliRef(() => this.nonnull(this.dereference(obj)).fields[key], value => this.field_set(obj, key, value));
    }
    function_pointer(methodId, receiver, virtual = false) {
        const method = this.meta.methods[methodId];
        if (virtual) {
            this.nonnull(receiver);
            if (method.virtual) methodId = this.meta.types[receiver.type]?.vtable?.[method.slot] ?? methodId;
        }
        return new CliFunction(methodId);
    }
    new_object(methodId, args) {
        const method = this.meta.methods[methodId], descriptor = this.meta.types[method.type];
        if (method.intrinsic === 'delegate.ctor') {
            if (!(args[1] instanceof CliFunction)) throw new TypeError('Delegate construction requires a translated method pointer');
            return new CliDelegate(method.type, [[args[0], args[1].method]]);
        }
        if (descriptor?.valueType) {
            this.ensure(method.type);
            const cell = [this.default(method.type)];
            this.call(methodId, [this.cell_ref(cell, 0, method.type), ...args]);
            return cell[0];
        }
        return super.new_object(methodId, args);
    }
    assignable(actual, target) {
        if (actual === target) return true;
        if (actual.endsWith('[]') && target.endsWith('[]')) {
            const a = actual.slice(0, -2), b = target.slice(0, -2);
            return this.default(a) === null && this.default(b) === null && this.assignable(a, b);
        }
        const todo = [actual], seen = new Set();
        while (todo.length) {
            const current = todo.pop();
            if (current === target) return true;
            if (current == null || seen.has(current)) continue;
            seen.add(current);
            const descriptor = this.meta.types[current];
            const expected = this.meta.types[target];
            if (descriptor && expected && descriptor.definition === expected.definition && descriptor.arguments.length && descriptor.variance.length === descriptor.arguments.length) {
                const reference = type => this.default(type) === null;
                if (descriptor.arguments.every((a, i) => {
                    const b = expected.arguments[i], v = descriptor.variance[i];
                    return a === b || reference(a) && reference(b) && (v === 1 ? this.assignable(a, b) : v === 2 && this.assignable(b, a));
                })) return true;
            }
            if (descriptor) todo.push(...descriptor.interfaces);
            todo.push(this.parents[current]);
        }
        return false;
    }
    enumerable_element(type) {
        const descriptor = this.meta.types[type];
        return descriptor?.definition === 'System.Collections.Generic.IEnumerable`1' ? descriptor.arguments[0] : null;
    }
    is_type(value, target) {
        if (value instanceof CliArray || value instanceof CliString) {
            if (target === 'System.Collections.IEnumerable') return true;
            const wanted = this.enumerable_element(target), actual = value instanceof CliArray ? value.element : 'System.Char';
            if (wanted !== null) return actual === wanted || this.default(actual) === null && this.default(wanted) === null && this.assignable(actual, wanted);
        }
        if (value instanceof CliDelegate) return ['System.Object', 'System.Delegate', 'System.MulticastDelegate', value.type].includes(target);
        if (value instanceof CliBox) return target === 'System.Object' || target === 'System.ValueType' || this.assignable(value.type, target);
        return super.is_type(value, target);
    }
    box(value, type) { return this.default(type) !== null ? new CliBox(type, this.coerce(value, type)) : value; }
    unbox(value, type, address = false) {
        if (this.default(type) === null && !address) return this.cast(value, type);
        this.nonnull(value);
        if (!(value instanceof CliBox) || value.type !== type) this.fail('System.InvalidCastException', 'Specified cast is not valid.');
        return address ? new CliRef(() => value.value, v => { value.value = this.coerce(v, type); }) : this.coerce(value.value, type);
    }
    call(methodId, args, virtual = false, constrained = null) {
        let method = this.meta.methods[methodId];
        if (constrained !== null && method.intrinsic === 'object.string') return this.string(this.format(this.nonnull(this.dereference(args[0])), constrained));
        if (virtual) {
            const original = this.nonnull(args[0]); let receiver = this.nonnull(this.dereference(original));
            const wanted = this.enumerable_element(method.type);
            if ((receiver instanceof CliArray || receiver instanceof CliString) && method.name === 'GetEnumerator' && (wanted !== null || method.type === 'System.Collections.IEnumerable')) {
                if (receiver instanceof CliString) receiver = new CliArray('System.Char', Array.from({length: receiver.text.length}, (_, i) => receiver.text.charCodeAt(i)));
                const constructor = this.meta.arrayEnumerators[(wanted ?? receiver.element) + '[]'];
                if (!constructor) throw new Error('Array enumeration contract was not linked');
                return this.new_object(constructor, [receiver]);
            }
            const actualType = receiver instanceof CliObject || receiver instanceof CliBox ? receiver.type : null;
            if (method.virtual && actualType) {
                methodId = this.meta.types[actualType]?.vtable?.[method.slot] ?? methodId;
                method = this.meta.methods[methodId];
            }
            const descriptor = this.meta.types[method.type];
            if (descriptor?.valueType) {
                if (receiver instanceof CliBox) args = [this.unbox(receiver, receiver.type, true), ...args.slice(1)];
                else if (original instanceof CliRef) args = [original, ...args.slice(1)];
                else throw new TypeError('Value-type method requires a managed address');
            } else args = [receiver, ...args.slice(1)];
        }
        return super.call(methodId, args, false);
    }
    async await_export(name, args, options = {}) {
        const maxSteps = options.maxSteps ?? 100000;
        if (!Number.isSafeInteger(maxSteps) || maxSteps < 1) throw new TypeError('maxSteps must be a positive integer');
        const yieldHost = options.yieldHost ?? (() => new Promise(resolve => setTimeout(resolve, 0)));
        if (typeof yieldHost !== 'function') throw new TypeError('yieldHost must be a function');
        const candidates = Object.entries(this.meta.exports).filter(([k]) => k === name || k.split('(')[0] === name);
        if (candidates.length !== 1) throw new Error('Use an unambiguous exported method signature');
        const binding = this.meta.asyncBindings[this.meta.methods[candidates[0][1]].returns];
        const value = this.invoke_export(name, args);
        if (!binding) return value;
        for (let step = 0; step < maxSteps; ++step) {
            options.signal?.throwIfAborted();
            if (this.call(binding.completed, [value])) {
                const cell = [this.call(binding.getAwaiter, [value])];
                const result = this.call(binding.getResult, [this.cell_ref(cell, 0, binding.awaiterType)]);
                if (result instanceof CliString) return result.text;
                return binding.resultType === 'System.Boolean' ? Boolean(result) : result;
            }
            this.call(binding.pump, []);
            await yieldHost();
        }
        throw new Error('Cooperative task exceeded the host pump step budget');
    }
    delegate_equal(a, b) {
        if (a === b) return true;
        if (a === null || b === null || a.type !== b.type || a.invocations.length !== b.invocations.length) return false;
        return a.invocations.every((x, i) => x[0] === b.invocations[i][0] && x[1] === b.invocations[i][1]);
    }
    external(method, args) {
        const op = method.intrinsic;
        if (op === 'environment.thread') return 1;
        if (op === 'delegate.invoke') {
            const delegate = this.nonnull(args[0]); let result = null;
            for (const [target, pointer] of delegate.invocations) {
                const callee = this.meta.methods[pointer]; let receiver = target;
                if (callee.instance && this.meta.types[callee.type]?.valueType) receiver = this.unbox(target, callee.type, true);
                result = this.call(pointer, [...(callee.instance ? [receiver] : []), ...args.slice(1)]);
            }
            return result;
        }
        if (op === 'delegate.equals' || op === 'delegate.not-equals') {
            const result = this.delegate_equal(args[0], args[1]); return Number(op === 'delegate.not-equals' ? !result : result);
        }
        if (op === 'delegate.combine' || op === 'delegate.remove') {
            const [a, b] = args;
            if (a === null) return op === 'delegate.combine' ? b : null;
            if (b === null) return a;
            if (a.type !== b.type) this.fail('System.ArgumentException', 'Delegates must be of the same type.');
            if (op === 'delegate.combine') return new CliDelegate(a.type, [...a.invocations, ...b.invocations]);
            for (let start = a.invocations.length - b.invocations.length; start >= 0; --start) {
                const part = new CliDelegate(a.type, a.invocations.slice(start, start + b.invocations.length));
                if (this.delegate_equal(part, b)) {
                    const remaining = [...a.invocations.slice(0, start), ...a.invocations.slice(start + b.invocations.length)];
                    return remaining.length ? new CliDelegate(a.type, remaining) : null;
                }
            }
            return a;
        }
        return super.external(method, args);
    }

}

class CliFunction { constructor(method) { this.method = method; } }
class CliDelegate { constructor(type, invocations) { this.type = type; this.invocations = invocations; } }
