# Typed value operations. Collections and comparer classes remain translated managed IL.
import struct as _struct

class ValueRuntime(HostedRuntime):
    def nullable(self, type_name):
        t = self.meta['types'].get(type_name)
        return t if t and t['definition'] == 'System.Nullable`1' else None

    def nullable_parts(self, value, type_name):
        t = self.nullable(type_name)
        if not t: return None
        has = next(k for k in t['fields'] if self.meta['fields'][k]['owner'] == type_name and k.endswith('::hasValue'))
        data = next(k for k in t['fields'] if self.meta['fields'][k]['owner'] == type_name and k.endswith('::value'))
        return (t['arguments'][0], value.fields[has] != 0, value.fields[data], has, data)

    def box(self, value, type_name):
        parts = self.nullable_parts(value, type_name)
        if parts: return super().box(parts[2], parts[0]) if parts[1] else None
        return super().box(value, type_name)

    def unbox(self, value, type_name, address=False):
        if self.nullable(type_name) and not address:
            result = self.default(type_name)
            parts = self.nullable_parts(result, type_name)
            if value is not None:
                result.fields[parts[3]] = 1
                result.fields[parts[4]] = super().unbox(value, parts[0], False)
            return result
        return super().unbox(value, type_name, address)

    def is_type(self, value, target):
        t = self.nullable(target)
        return super().is_type(value, t['arguments'][0] if t else target)

    def actual_type(self, value, fallback='System.Object'):
        value = self.dereference(value)
        if isinstance(value, CliString): return 'System.String'
        if isinstance(value, CliArray): return value.element + '[]'
        return getattr(value, 'type', fallback)

    def object_slot(self, value, name):
        return self.meta['types'].get(self.actual_type(value), {}).get('objectSlots', {}).get(name)

    def invoke_slot(self, method_id, receiver, rest):
        method = self.meta['methods'][method_id]
        target = self.dereference(receiver)
        if self.meta['types'].get(method['type'], {}).get('valueType'):
            if isinstance(target, CliBox): receiver = self.unbox(target, target.type, True)
            elif not isinstance(receiver, CliRef): receiver = self.cell_ref([target], 0, method['type'])
        else: receiver = target
        return super().call(method_id, [receiver] + rest, False)

    def call(self, method_id, args, virtual=False, constrained=None):
        method = self.meta['methods'][method_id]
        name = {'object.string':'ToString', 'object.hash':'GetHashCode', 'object.equals':'Equals'}.get(method['intrinsic'])
        if virtual and name:
            self.nonnull(self.dereference(args[0]))
            slot = self.object_slot(args[0], name)
            if slot: return self.invoke_slot(slot, args[0], args[1:])
            if name == 'Equals':
                receiver = self.dereference(args[0])
                return self.object_equal(self.box(receiver, receiver.type) if isinstance(receiver, CliValue) else receiver, args[1], False)
        return super().call(method_id, args, virtual, constrained)

    def format(self, value, type_name='System.Object'):
        slot = self.object_slot(value, 'ToString') if value is not None else None
        if slot:
            result = self.invoke_slot(slot, value, [])
            return '' if result is None else self.nonnull(result).text
        return super().format(value, type_name)

    def string_hash(self, text):
        hash_code = 2166136261
        units = text.encode('utf-16-le', 'surrogatepass')
        for i in range(0, len(units), 2):
            hash_code = ((hash_code ^ int.from_bytes(units[i:i+2], 'little')) * 16777619) & 0xffffffff
        return self.bits(hash_code, 32)

    @staticmethod
    def primitive_equal(a, b):
        return a == b or isinstance(a, float) and isinstance(b, float) and math.isnan(a) and math.isnan(b)

    def object_equal(self, a, b, dispatch=True):
        a, b = self.dereference(a), self.dereference(b)
        if a is b: return 1
        if a is None or b is None: return 0
        if dispatch:
            slot = self.object_slot(a, 'Equals')
            if slot: return self.invoke_slot(slot, a, [b])
        if isinstance(a, CliString): return int(isinstance(b, CliString) and a.text == b.text)
        if isinstance(a, CliDelegate): return int(isinstance(b, CliDelegate) and self.delegate_equal(a, b))
        if isinstance(a, CliBox):
            if not isinstance(b, CliBox) or a.type != b.type: return 0
            return self.fields_equal(a.value, b.value, a.type)
        return 0

    def fields_equal(self, a, b, type_name):
        if not isinstance(a, CliValue): return int(self.primitive_equal(a, b))
        if not isinstance(b, CliValue) or a.type != b.type: return 0
        for key in self.meta['types'][type_name]['fields']:
            field = self.meta['fields'][key]
            if field['static']: continue
            if not self.object_equal(self.box(a.fields[key], field['type']), self.box(b.fields[key], field['type'])): return 0
        return 1

    def contract_slot(self, type_name, definition, name):
        if type_name not in self.meta['types']: return None
        contract = definition if definition == 'System.IComparable' else definition + '<' + type_name + '>'
        if not self.assignable(type_name, contract): return None
        for method_id, method in self.meta['methods'].items():
            if method['type'] == contract and method['name'] == name: return method_id
        raise RuntimeError('Semantic interface was not rooted: ' + contract)

    def semantic_equal(self, a, b, type_name):
        pa, pb = self.nullable_parts(a, type_name), self.nullable_parts(b, type_name)
        if pa: return int(pa[1] == pb[1] and (not pa[1] or self.semantic_equal(pa[2], pb[2], pa[0])))
        if a is None or b is None: return int(a is b)
        method = self.contract_slot(type_name, 'System.IEquatable`1', 'Equals')
        if method:
            receiver = self.cell_ref([self.copy_value(a)], 0, type_name) if self.meta['types'].get(type_name, {}).get('valueType') else a
            return self.call(method, [receiver, b], True, type_name)
        return self.object_equal(self.box(a, type_name), self.box(b, type_name))

    def semantic_hash(self, value, type_name, dispatch=True):
        value = self.dereference(value)
        if value is None: return 0
        if dispatch:
            slot = self.object_slot(value, 'GetHashCode')
            if slot: return self.invoke_slot(slot, value, [])
        if isinstance(value, CliBox): return self.semantic_hash(value.value, value.type, False)
        if isinstance(value, CliString): return self.string_hash(value.text)
        nullable = self.nullable_parts(value, type_name)
        if nullable: return self.semantic_hash(nullable[2], nullable[0]) if nullable[1] else 0
        if isinstance(value, float):
            if value == 0 or math.isnan(value): return 2146435072 if math.isnan(value) else 0
            bits = int.from_bytes(_struct.pack('<d', value), 'little')
            return self.bits(bits ^ (bits >> 32), 32)
        if isinstance(value, int):
            if type_name in ('System.Int64', 'System.UInt64'): return self.bits(value ^ (value >> 32), 32)
            if type_name == 'System.Char': return self.bits(value | value << 16, 32)
            return self.bits(value, 32)
        if isinstance(value, CliValue):
            hash_code = 0
            for key in self.meta['types'][value.type]['fields']:
                field = self.meta['fields'][key]
                if not field['static']: hash_code = self.bits(hash_code * 31 + self.semantic_hash(value.fields[key], field['type']), 32)
            return hash_code
        if isinstance(value, CliDelegate):
            hash_code = self.string_hash(value.type)
            for target, method in value.invocations:
                hash_code = self.bits(hash_code * 31 + self.semantic_hash(target, 'System.Object') + self.string_hash(method), 32)
            return hash_code
        return super().external({'intrinsic':'object.identity-hash'}, [value])

    @staticmethod
    def ordinal_compare(a, b):
        if a is b: return 0
        if a is None: return -1
        if b is None: return 1
        x, y = a.text.encode('utf-16-le', 'surrogatepass'), b.text.encode('utf-16-le', 'surrogatepass')
        for i in range(0, min(len(x), len(y)), 2):
            difference = int.from_bytes(x[i:i+2], 'little') - int.from_bytes(y[i:i+2], 'little')
            if difference: return difference
        return (len(x) - len(y)) // 2

    def semantic_compare(self, a, b, type_name):
        na, nb = self.nullable_parts(a, type_name), self.nullable_parts(b, type_name)
        if na: return (self.semantic_compare(na[2], nb[2], na[0]) if nb[1] else 1) if na[1] else (-1 if nb[1] else 0)
        if a is None or b is None: return 0 if a is b else -1 if a is None else 1
        if isinstance(a, (int, float)):
            if isinstance(a, float) and math.isnan(a): return 0 if isinstance(b, float) and math.isnan(b) else -1
            if isinstance(b, float) and math.isnan(b): return 1
            return 0 if a == b else -1 if a < b else 1
        if isinstance(a, CliString):
            raise RuntimeError('TR2300: Culture-dependent string ordering is unavailable; use StringComparer.Ordinal or an explicit comparer.')
        if isinstance(a, CliBox) and isinstance(a.value, (int, float)):
            if not isinstance(b, CliBox) or a.type != b.type: self.fail('System.ArgumentException', 'Objects must have compatible comparison types.')
            return self.semantic_compare(a.value, b.value, a.type)
        method = self.contract_slot(type_name, 'System.IComparable`1', 'CompareTo')
        if method:
            receiver = self.cell_ref([self.copy_value(a)], 0, type_name) if self.meta['types'].get(type_name, {}).get('valueType') else a
            return self.call(method, [receiver, b], True, type_name)
        method = self.contract_slot(self.actual_type(a, type_name), 'System.IComparable', 'CompareTo')
        if method: return self.call(method, [self.box(a, type_name), self.box(b, type_name)], True)
        self.fail('System.ArgumentException', 'At least one object must implement IComparable.')

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'value.equal': return self.semantic_equal(args[0], args[1], method['arguments'][0])
        if op == 'value.hash': return self.semantic_hash(args[0], method['arguments'][0])
        if op == 'value.compare': return self.semantic_compare(args[0], args[1], method['arguments'][0])
        if op == 'value.compare-instance': return self.semantic_compare(self.dereference(args[0]), args[1], method['type'])
        if op == 'string.compare-ordinal': return self.ordinal_compare(args[0], args[1])
        if op == 'object.equals-static': return self.object_equal(args[0], args[1])
        if op == 'object.equals':
            a, b = self.nonnull(self.dereference(args[0])), args[1]
            if method['type'] == 'System.Object': return int(a is b)
            if method['type'] in ('System.ValueType', 'System.Enum'): return self.object_equal(a, b, False)
            if method['type'] == 'System.String': return self.object_equal(a, b, False)
            value = a.value if isinstance(a, CliBox) else a
            if method['params'][0] == 'System.Object': return int(isinstance(b, CliBox) and b.type == method['type'] and self.primitive_equal(value, b.value))
            return self.semantic_equal(value, b, method['type'])
        if op == 'object.hash': return self.semantic_hash(self.nonnull(args[0]), method['type'], False)
        if op == 'object.string':
            value = self.nonnull(self.dereference(args[0]))
            return self.string(super().format(value, value.type if isinstance(value, CliBox) else method['type']))
        return super().external(method, args)
