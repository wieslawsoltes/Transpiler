# Managed storage semantics shared by ordinary code and translated C# library implementations.
class CliValue(CliObject):
    __slots__ = ()


class ManagedRuntime(CliRuntime):
    def __init__(self, metadata, write=None):
        super().__init__(metadata, write)
        self.parents['System.ArgumentNullException'] = 'System.ArgumentException'
        self.parents['System.OperationCanceledException'] = 'System.SystemException'
        self.parents['System.Threading.Tasks.TaskCanceledException'] = 'System.OperationCanceledException'

    def default(self, type_name):
        descriptor = self.meta['types'].get(type_name)
        if descriptor and descriptor['enumType']:
            return super().default(descriptor['enumType'])
        if descriptor and descriptor['valueType']:
            value = CliValue(type_name)
            for key in descriptor['fields']:
                field = self.meta['fields'][key]
                if not field['static']: value.fields[key] = self.default(field['type'])
            return value
        return super().default(type_name)

    def copy_value(self, value):
        if not isinstance(value, CliValue): return value
        copy = CliValue(value.type)
        copy.fields = {key: self.copy_value(item) for key, item in value.fields.items()}
        return copy

    def coerce(self, value, type_name):
        descriptor = self.meta['types'].get(type_name)
        if descriptor and descriptor['enumType']:
            return super().coerce(value, descriptor['enumType'])
        if descriptor and descriptor['valueType']:
            if not isinstance(value, CliValue) or value.type != type_name:
                raise TypeError('Invalid managed value representation for ' + type_name)
            return self.copy_value(value)
        return super().coerce(value, type_name)

    @staticmethod
    def dereference(value):
        return value.get() if isinstance(value, CliRef) else value

    def format(self, value, type_name='System.Object'):
        value = self.dereference(value)
        if isinstance(value, CliBox): return self.format(value.value, value.type)
        descriptor = self.meta['types'].get(type_name)
        if descriptor and descriptor['enumType']:
            return self.enum_text(value, descriptor)
        if isinstance(value, CliObject):
            name = value.type
            if name.startswith('['): name = name[name.index(']') + 1:]
            return name.replace('<', '[').replace('>', ']')
        return super().format(value, type_name)

    @staticmethod
    def enum_text(value, descriptor):
        width = 64 if descriptor['enumType'] in ('System.Int64', 'System.UInt64') else 32
        bits = int(value) & ((1 << width) - 1)
        entries = [(int(item['value']) & ((1 << width) - 1), item['name']) for item in descriptor['enumValues']]
        entries.sort(key=lambda item: item[0])
        for number, name in entries:
            if number == bits: return name
        if descriptor['enumFlags'] and bits:
            remaining, names = bits, []
            for number, name in reversed(entries):
                if number and remaining & number == number:
                    remaining &= ~number
                    names.insert(0, name)
            if remaining == 0: return ', '.join(names)
        return str(value)

    def field_get(self, obj, key):
        value = super().field_get(self.dereference(obj), key)
        return self.coerce(value, self.meta['fields'][key]['type'])

    def field_set(self, obj, key, value):
        super().field_set(self.dereference(obj), key, value)

    def field_ref(self, obj, key):
        field = self.meta['fields'][key]
        if field['static']:
            self.ensure(field['owner'])
            return self.cell_ref(self.statics, key, field['type'])
        self.nonnull(self.dereference(obj))
        # Re-evaluate the parent address on every access: s = new S() must not detach ref s.Field.
        return CliRef(lambda: self.nonnull(self.dereference(obj)).fields[key],
                      lambda value: self.field_set(obj, key, value))

    def function_pointer(self, method_id, receiver, virtual=False):
        method = self.meta['methods'][method_id]
        if virtual:
            receiver = self.nonnull(receiver)
            if method['virtual']:
                method_id = self.meta['types'].get(receiver.type, {}).get('vtable', {}).get(method['slot'], method_id)
        return CliFunction(method_id)

    def new_object(self, method_id, args):
        method = self.meta['methods'][method_id]
        if method['intrinsic'] == 'delegate.ctor':
            if not isinstance(args[1], CliFunction): raise TypeError('Delegate construction requires a translated method pointer')
            return CliDelegate(method['type'], [(args[0], args[1].method)])
        descriptor = self.meta['types'].get(method['type'])
        if descriptor and descriptor['valueType']:
            self.ensure(method['type'])
            cell = [self.default(method['type'])]
            self.call(method_id, [self.cell_ref(cell, 0, method['type'])] + args)
            return cell[0]
        return super().new_object(method_id, args)

    def assignable(self, actual, target):
        if actual == target: return True
        if actual.endswith('[]') and target.endswith('[]'):
            a, b = actual[:-2], target[:-2]
            return self.default(a) is None and self.default(b) is None and self.assignable(a, b)
        todo, seen = [actual], set()
        while todo:
            current = todo.pop()
            if current == target: return True
            if current is None or current in seen: continue
            seen.add(current)
            descriptor = self.meta['types'].get(current)
            expected = self.meta['types'].get(target)
            if descriptor and expected and descriptor['definition'] == expected['definition'] and descriptor['arguments']:
                def reference(name): return self.default(name) is None
                def matches(a, b, variance):
                    return a == b or reference(a) and reference(b) and ((variance == 1 and self.assignable(a, b)) or (variance == 2 and self.assignable(b, a)))
                if all(matches(a, b, v) for a, b, v in zip(descriptor['arguments'], expected['arguments'], descriptor['variance'])) and len(descriptor['variance']) == len(descriptor['arguments']):
                    return True
            if descriptor: todo.extend(descriptor['interfaces'])
            todo.append(self.parents.get(current))
        return False

    def enumerable_element(self, type_name):
        descriptor = self.meta['types'].get(type_name)
        if descriptor and descriptor['definition'] == 'System.Collections.Generic.IEnumerable`1': return descriptor['arguments'][0]
        return None

    def is_type(self, value, target):
        if isinstance(value, (CliArray, CliString)):
            if target == 'System.Collections.IEnumerable': return True
            wanted = self.enumerable_element(target)
            actual = value.element if isinstance(value, CliArray) else 'System.Char'
            if wanted is not None:
                return actual == wanted or self.default(actual) is None and self.default(wanted) is None and self.assignable(actual, wanted)
        if isinstance(value, CliDelegate): return target in ('System.Object', 'System.Delegate', 'System.MulticastDelegate', value.type)
        if isinstance(value, CliBox): return target in ('System.Object', 'System.ValueType') or self.assignable(value.type, target)
        return super().is_type(value, target)

    def box(self, value, type_name):
        if self.default(type_name) is not None: return CliBox(type_name, self.coerce(value, type_name))
        return value

    def unbox(self, value, type_name, address=False):
        if self.default(type_name) is None and not address: return self.cast(value, type_name)
        self.nonnull(value)
        if not isinstance(value, CliBox) or value.type != type_name:
            self.fail('System.InvalidCastException', 'Specified cast is not valid.')
        if address: return CliRef(lambda: value.value, lambda v: setattr(value, 'value', self.coerce(v, type_name)))
        return self.coerce(value.value, type_name)

    def call(self, method_id, args, virtual=False, constrained=None):
        method = self.meta['methods'][method_id]
        if constrained is not None and method['intrinsic'] == 'object.string':
            return self.string(self.format(self.nonnull(self.dereference(args[0])), constrained))
        if virtual:
            original = self.nonnull(args[0])
            receiver = self.nonnull(self.dereference(original))
            wanted = self.enumerable_element(method['type'])
            if isinstance(receiver, (CliArray, CliString)) and method['name'] == 'GetEnumerator' and (wanted is not None or method['type'] == 'System.Collections.IEnumerable'):
                if isinstance(receiver, CliString):
                    units = receiver.text.encode('utf-16-le', 'surrogatepass')
                    receiver = CliArray('System.Char', [int.from_bytes(units[i:i+2], 'little') for i in range(0, len(units), 2)])
                constructor = self.meta['arrayEnumerators'].get((wanted or receiver.element) + '[]')
                if constructor is None: raise RuntimeError('Array enumeration contract was not linked')
                return self.new_object(constructor, [receiver])
            actual_type = receiver.type if isinstance(receiver, (CliObject, CliBox)) else None
            if method['virtual'] and actual_type:
                method_id = self.meta['types'].get(actual_type, {}).get('vtable', {}).get(method['slot'], method_id)
                method = self.meta['methods'][method_id]
            descriptor = self.meta['types'].get(method['type'])
            if descriptor and descriptor['valueType']:
                if isinstance(receiver, CliBox): args = [self.unbox(receiver, receiver.type, True)] + args[1:]
                elif isinstance(original, CliRef): args = [original] + args[1:]
                else: raise TypeError('Value-type method requires a managed address')
            else: args = [receiver] + args[1:]
        return super().call(method_id, args, False)

    async def await_export(self, name, args, max_steps=100000):
        import asyncio
        if not isinstance(max_steps, int) or max_steps < 1: raise ValueError('max_steps must be a positive integer')
        candidates = [(k, v) for k, v in self.meta['exports'].items() if k == name or k.split('(')[0] == name]
        if len(candidates) != 1: raise ValueError('Use an unambiguous exported method signature')
        result_type = self.meta['methods'][candidates[0][1]]['returns']
        binding = self.meta['asyncBindings'].get(result_type)
        value = self.invoke_export(name, args)
        if binding is None: return value
        for _ in range(max_steps):
            if self.call(binding['completed'], [value]):
                cell = [self.call(binding['getAwaiter'], [value])]
                result = self.call(binding['getResult'], [self.cell_ref(cell, 0, binding['awaiterType'])])
                if isinstance(result, CliString): return result.text
                return bool(result) if binding['resultType'] == 'System.Boolean' else result
            self.call(binding['pump'], [])
            await asyncio.sleep(0)
        raise TimeoutError('Cooperative task exceeded the host pump step budget')

    @staticmethod
    def delegate_equal(a, b):
        if a is b: return True
        if a is None or b is None or a.type != b.type or len(a.invocations) != len(b.invocations): return False
        return all(x[0] is y[0] and x[1] == y[1] for x, y in zip(a.invocations, b.invocations))

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'environment.thread': return 1
        if op == 'delegate.invoke':
            delegate = self.nonnull(args[0]); result = None
            for target, pointer in delegate.invocations:
                callee = self.meta['methods'][pointer]
                receiver = target
                if callee['instance'] and self.meta['types'].get(callee['type'], {}).get('valueType'):
                    receiver = self.unbox(target, callee['type'], True)
                result = self.call(pointer, ([receiver] if callee['instance'] else []) + args[1:])
            return result
        if op in ('delegate.equals', 'delegate.not-equals'):
            result = self.delegate_equal(args[0], args[1])
            return int(not result if op == 'delegate.not-equals' else result)
        if op in ('delegate.combine', 'delegate.remove'):
            a, b = args
            if a is None: return b if op == 'delegate.combine' else None
            if b is None: return a
            if a.type != b.type: self.fail('System.ArgumentException', 'Delegates must be of the same type.')
            if op == 'delegate.combine': return CliDelegate(a.type, a.invocations + b.invocations)
            for start in range(len(a.invocations) - len(b.invocations), -1, -1):
                part = CliDelegate(a.type, a.invocations[start:start + len(b.invocations)])
                if self.delegate_equal(part, b):
                    remaining = a.invocations[:start] + a.invocations[start + len(b.invocations):]
                    return CliDelegate(a.type, remaining) if remaining else None
            return a
        return super().external(method, args)


class CliFunction:
    __slots__ = ('method',)
    def __init__(self, method): self.method = method


class CliDelegate:
    __slots__ = ('type', 'invocations')
    def __init__(self, type_name, invocations): self.type, self.invocations = type_name, tuple(invocations)
