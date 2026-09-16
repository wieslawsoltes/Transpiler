// Typed value operations and external Object slots. Collection algorithms remain translated managed IL.
class ValueRuntime extends HostedRuntime {
    nullable(type) { const t = this.meta.types[type]; return t?.definition === 'System.Nullable`1' ? t : null; }
    nullable_parts(value, type) {
        const t = this.nullable(type);
        if (!t) return null;
        const has = t.fields.find(k => this.meta.fields[k].owner === type && k.endsWith('::hasValue'));
        const data = t.fields.find(k => this.meta.fields[k].owner === type && k.endsWith('::value'));
        return [t.arguments[0], value.fields[has] !== 0, value.fields[data], has, data];
    }
    box(value, type) {
        const parts = this.nullable_parts(value, type);
        return parts ? parts[1] ? super.box(parts[2], parts[0]) : null : super.box(value, type);
    }
    unbox(value, type, address = false) {
        if (this.nullable(type) && !address) {
            const result = this.default(type), parts = this.nullable_parts(result, type);
            if (value !== null) {
                result.fields[parts[3]] = 1;
                result.fields[parts[4]] = super.unbox(value, parts[0], false);
            }
            return result;
        }
        return super.unbox(value, type, address);
    }
    is_type(value, target) {
        const t = this.nullable(target);
        return t ? super.is_type(value, t.arguments[0]) : super.is_type(value, target);
    }
    actual_type(value, fallback = 'System.Object') {
        value = this.dereference(value);
        if (value instanceof CliString) return 'System.String';
        if (value instanceof CliArray) return value.element + '[]';
        return value?.type ?? fallback;
    }
    object_slot(value, name) { return this.meta.types[this.actual_type(value)]?.objectSlots?.[name] ?? null; }
    invoke_slot(id, receiver, rest) {
        const m = this.meta.methods[id], target = this.dereference(receiver);
        if (this.meta.types[m.type]?.valueType) {
            if (target instanceof CliBox) receiver = this.unbox(target, target.type, true);
            else if (!(receiver instanceof CliRef)) receiver = this.cell_ref([target], 0, m.type);
        } else receiver = target;
        return super.call(id, [receiver, ...rest], false);
    }
    call(id, args, virtual = false, constrained = null) {
        const m = this.meta.methods[id];
        const name = {'object.string': 'ToString', 'object.hash': 'GetHashCode', 'object.equals': 'Equals'}[m.intrinsic];
        if (virtual && name) {
            this.nonnull(this.dereference(args[0]));
            const slot = this.object_slot(args[0], name);
            if (slot) return this.invoke_slot(slot, args[0], args.slice(1));
            if (name === 'Equals') {
                const receiver = this.dereference(args[0]);
                return this.object_equal(receiver instanceof CliValue ? this.box(receiver, receiver.type) : receiver, args[1], false);
            }
        }
        return super.call(id, args, virtual, constrained);
    }
    format(value, type = 'System.Object') {
        const slot = value == null ? null : this.object_slot(value, 'ToString');
        if (slot) {
            const result = this.invoke_slot(slot, value, []);
            return result === null ? '' : this.nonnull(result).text;
        }
        return super.format(value, type);
    }
    // String hashes are runtime-local by contract. Hash UTF-16 code units, not Unicode scalar values.
    string_hash(text) {
        let hash = 2166136261;
        for (let i = 0; i < text.length; i++) hash = Math.imul(hash ^ text.charCodeAt(i), 16777619);
        return hash | 0;
    }
    primitive_equal(a, b) { return a === b || typeof a === 'number' && Number.isNaN(a) && Number.isNaN(b); }
    object_equal(a, b, dispatch = true) {
        a = this.dereference(a); b = this.dereference(b);
        if (a === b) return 1;
        if (a === null || b === null) return 0;
        if (dispatch) { const slot = this.object_slot(a, 'Equals'); if (slot) return this.invoke_slot(slot, a, [b]); }
        if (a instanceof CliString) return Number(b instanceof CliString && a.text === b.text);
        if (a instanceof CliDelegate) return Number(b instanceof CliDelegate && this.delegate_equal(a, b));
        if (a instanceof CliBox) {
            if (!(b instanceof CliBox) || a.type !== b.type) return 0;
            return this.fields_equal(a.value, b.value, a.type);
        }
        return 0;
    }
    fields_equal(a, b, type) {
        if (!(a instanceof CliValue)) return Number(this.primitive_equal(a, b));
        if (!(b instanceof CliValue) || a.type !== b.type) return 0;
        for (const key of this.meta.types[type].fields) {
            const field = this.meta.fields[key]; if (field.static) continue;
            const x = this.box(a.fields[key], field.type), y = this.box(b.fields[key], field.type);
            if (!this.object_equal(x, y)) return 0;
        }
        return 1;
    }
    contract_slot(type, definition, name) {
        const descriptor = this.meta.types[type];
        if (!descriptor) return null;
        const contract = definition === 'System.IComparable' ? definition : definition + '<' + type + '>';
        if (!this.assignable(type, contract)) return null;
        for (const [id, m] of Object.entries(this.meta.methods))
            if (m.type === contract && m.name === name) return id;
        throw new Error('Semantic interface was not rooted: ' + contract);
    }
    semantic_equal(a, b, type) {
        const partsA = this.nullable_parts(a, type), partsB = this.nullable_parts(b, type);
        if (partsA) return Number(partsA[1] === partsB[1] && (!partsA[1] || this.semantic_equal(partsA[2], partsB[2], partsA[0])));
        if (a === null || b === null) return Number(a === b);
        const id = this.contract_slot(type, 'System.IEquatable`1', 'Equals');
        if (id) {
            // Dispatch uses the declared T's contract, not an interface found only on a runtime subtype.
            let receiver = a;
            if (this.meta.types[type]?.valueType) receiver = this.cell_ref([this.copy_value(a)], 0, type);
            return this.call(id, [receiver, b], true, type);
        }
        return this.object_equal(this.box(a, type), this.box(b, type));
    }
    semantic_hash(value, type, dispatch = true) {
        value = this.dereference(value);
        if (value === null) return 0;
        if (dispatch) { const slot = this.object_slot(value, 'GetHashCode'); if (slot) return this.invoke_slot(slot, value, []); }
        if (value instanceof CliBox) return this.semantic_hash(value.value, value.type, false);
        if (value instanceof CliString) return this.string_hash(value.text);
        const nullable = this.nullable_parts(value, type);
        if (nullable) return nullable[1] ? this.semantic_hash(nullable[2], nullable[0]) : 0;
        if (typeof value === 'bigint') return Number(BigInt.asIntN(32, value ^ (value >> 32n)));
        if (typeof value === 'number') {
            if (type === 'System.Double' || type === 'System.Single') {
                if (value === 0 || Number.isNaN(value)) return Number.isNaN(value) ? 2146435072 : 0;
                const buffer = new ArrayBuffer(8), view = new DataView(buffer);
                view.setFloat64(0, value, true);
                return view.getInt32(0, true) ^ view.getInt32(4, true);
            }
            if (type === 'System.Char') return (value | (value << 16)) | 0;
            return value | 0;
        }
        if (value instanceof CliValue) {
            let hash = 0;
            for (const key of this.meta.types[value.type].fields) {
                const field = this.meta.fields[key];
                if (!field.static) hash = (Math.imul(hash, 31) + this.semantic_hash(value.fields[key], field.type)) | 0;
            }
            return hash;
        }
        if (value instanceof CliDelegate) {
            let hash = this.string_hash(value.type);
            for (const [target, method] of value.invocations)
                hash = (Math.imul(hash, 31) + this.semantic_hash(target, 'System.Object') + this.string_hash(method)) | 0;
            return hash;
        }
        return super.external({intrinsic:'object.identity-hash'}, [value]);
    }
    ordinal_compare(a, b) {
        if (a === b) return 0; if (a === null) return -1; if (b === null) return 1;
        const length = Math.min(a.text.length, b.text.length);
        for (let i = 0; i < length; i++) { const d = a.text.charCodeAt(i) - b.text.charCodeAt(i); if (d) return d; }
        return a.text.length - b.text.length;
    }
    semantic_compare(a, b, type) {
        const na = this.nullable_parts(a, type), nb = this.nullable_parts(b, type);
        if (na) return na[1] ? nb[1] ? this.semantic_compare(na[2], nb[2], na[0]) : 1 : nb[1] ? -1 : 0;
        if (a === null || b === null) return a === b ? 0 : a === null ? -1 : 1;
        if (typeof a === 'number' || typeof a === 'bigint') {
            if (Number.isNaN(a)) return Number.isNaN(b) ? 0 : -1;
            if (Number.isNaN(b)) return 1;
            return a === b ? 0 : a < b ? -1 : 1;
        }
        let id = this.contract_slot(type, 'System.IComparable`1', 'CompareTo');
        if (id) {
            let receiver = this.meta.types[type]?.valueType ? this.cell_ref([this.copy_value(a)], 0, type) : a;
            return this.call(id, [receiver, b], true, type);
        }
        id = this.contract_slot(type, 'System.IComparable', 'CompareTo');
        if (id) return this.call(id, [this.box(a,type), this.box(b,type)], true);
        this.fail('System.ArgumentException', 'At least one object must implement IComparable.');
    }
    external(m, args) {
        const op = m.intrinsic;
        if (op === 'value.equal') return this.semantic_equal(args[0], args[1], m.arguments[0]);
        if (op === 'value.hash') return this.semantic_hash(args[0], m.arguments[0]);
        if (op === 'value.compare') return this.semantic_compare(args[0], args[1], m.arguments[0]);
        if (op === 'value.compare-instance') return this.semantic_compare(this.dereference(args[0]), args[1], m.type);
        if (op === 'string.compare-ordinal') return this.ordinal_compare(args[0], args[1]);
        if (op === 'object.equals-static') return this.object_equal(args[0], args[1]);
        if (op === 'object.equals') {
            const a = this.nonnull(this.dereference(args[0])), b = args[1];
            if (m.type === 'System.Object') return Number(a === b); // A direct base.Equals call must not redispatch.
            if (m.type === 'System.ValueType' || m.type === 'System.Enum') return this.object_equal(a, b, false);
            if (m.type === 'System.String') return this.object_equal(a, b, false);
            const value = a instanceof CliBox ? a.value : a;
            if (m.params[0] === 'System.Object') return Number(b instanceof CliBox && b.type === m.type && this.primitive_equal(value, b.value));
            return this.semantic_equal(value, b, m.type);
        }
        if (op === 'object.hash') return this.semantic_hash(this.nonnull(args[0]), m.type, false);
        if (op === 'object.string') {
            // Nonvirtual base calls bypass the override bridge, including inside an override itself.
            const value = this.nonnull(this.dereference(args[0]));
            return this.string(super.format(value, value instanceof CliBox ? value.type : m.type));
        }
        return super.external(m, args);
    }
}
