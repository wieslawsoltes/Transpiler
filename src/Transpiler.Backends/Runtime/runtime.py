# Transpiler portable-mvp semantic helpers. Standard-library-only; no CLR or IL interpreter.
import math
import sys


class CliString:
    __slots__ = ('text',)
    def __init__(self, text):
        self.text = text.encode('utf-16-le', 'surrogatepass').decode('utf-16-le', 'surrogatepass')


class CliObject:
    __slots__ = ('type', 'fields')
    def __init__(self, type_name): self.type, self.fields = type_name, {}


class CliArray:
    __slots__ = ('element', 'data')
    def __init__(self, element, data): self.element, self.data = element, data


class CliBox:
    __slots__ = ('type', 'value')
    def __init__(self, type_name, value): self.type, self.value = type_name, value


class CliRef:
    __slots__ = ('get', 'set')
    def __init__(self, get, set): self.get, self.set = get, set


class CliError(Exception):
    def __init__(self, value):
        self.value = value
        super().__init__(value.type + ': ' + value.fields.get('$message', ''))


class CliFlow:
    """Per-frame exception search and finally/fault continuations. Filters are rejected upstream."""
    def __init__(self, runtime, clauses):
        self.runtime, self.clauses = runtime, clauses
        self.pending, self.caught, self.escaping = [], [], None

    def _finish(self, target, error, catcher, stack):
        stack.clear()
        if catcher is not None:
            stack.append(error.value)
            self.caught.append((catcher, error))
        if target is None:
            self.escaping = error
            return -1
        return target

    def _begin(self, handlers, target, error, catcher, stack):
        stack.clear()
        if not handlers: return self._finish(target, error, catcher, stack)
        self.pending.append({'todo': handlers[1:], 'target': target, 'error': error,
                             'catcher': catcher, 'active': handlers[0]})
        return handlers[0]['handlerStart']

    def leave(self, pc, target, stack):
        handlers = [c for c in self.clauses if c['kind'] == 'Finally' and
                    c['tryStart'] <= pc < c['tryEnd'] and not c['tryStart'] <= target < c['tryEnd']]
        handlers.sort(key=lambda c: c['tryEnd'] - c['tryStart'])
        return self._begin(handlers, target, None, None, stack)

    def handle(self, error, pc, stack):
        candidates = [c for c in self.clauses if c['kind'] == 'Catch' and
                      c['tryStart'] <= pc < c['tryEnd'] and self.runtime.is_type(error.value, c['catchType'])]
        candidates.sort(key=lambda c: c['tryEnd'] - c['tryStart'])
        catcher = candidates[0] if candidates else None
        target = catcher['handlerStart'] if catcher else None
        # A locally caught exception inside a finally preserves the earlier unwind continuation.
        self.pending[:] = [p for p in self.pending if target is not None and
                           p['active']['handlerStart'] <= target < p['active']['handlerEnd']]
        handlers = [c for c in self.clauses if c['kind'] in ('Finally', 'Fault') and
                    c['tryStart'] <= pc < c['tryEnd'] and
                    (target is None or not c['tryStart'] <= target < c['tryEnd'])]
        handlers.sort(key=lambda c: c['tryEnd'] - c['tryStart'])
        return self._begin(handlers, target, error, catcher, stack)

    def resume(self, stack):
        if not self.pending: raise RuntimeError('endfinally without a pending continuation')
        p = self.pending[-1]
        stack.clear()
        if p['todo']:
            p['active'] = p['todo'].pop(0)
            return p['active']['handlerStart']
        self.pending.pop()
        return self._finish(p['target'], p['error'], p['catcher'], stack)

    def rethrow(self, pc):
        for clause, error in reversed(self.caught):
            if clause['handlerStart'] <= pc < clause['handlerEnd']: raise error
        raise RuntimeError('rethrow outside a catch handler')


class CliRuntime:
    def __init__(self, metadata, write=None):
        self.meta, self.functions = metadata, {}
        self.statics, self.initialization, self.interned = {}, {}, {}
        self.write = write or sys.stdout.write
        self.parents = {'System.Object': None, 'System.String': 'System.Object',
                        'System.Exception': 'System.Object', 'System.SystemException': 'System.Exception',
                        'System.ArithmeticException': 'System.SystemException'}
        for t in ('DivideByZeroException', 'OverflowException'):
            self.parents['System.' + t] = 'System.ArithmeticException'
        for t in ('NullReferenceException', 'IndexOutOfRangeException', 'ArrayTypeMismatchException',
                  'InvalidCastException', 'ArgumentException', 'InvalidOperationException',
                  'NotSupportedException', 'TypeInitializationException', 'OutOfMemoryException'):
            self.parents['System.' + t] = 'System.SystemException'
        self.parents['System.ArgumentOutOfRangeException'] = 'System.ArgumentException'
        self.parents.update({k: v['base'] for k, v in metadata['types'].items()})
        for k, f in metadata['fields'].items():
            if f['static']: self.statics[k] = self.default(f['type'])

    def string(self, text, intern=False):
        if isinstance(text, CliString): return text
        text = text.encode('utf-16-le', 'surrogatepass').decode('utf-16-le', 'surrogatepass')
        if intern:
            if text not in self.interned: self.interned[text] = CliString(text)
            return self.interned[text]
        return CliString(text)

    @staticmethod
    def bits(value, width, unsigned=False):
        value = int(value) & ((1 << width) - 1)
        return value if unsigned or value < (1 << (width - 1)) else value - (1 << width)

    def fail(self, type_name, message=''):
        obj = CliObject(type_name)
        obj.fields['$message'] = message
        raise CliError(obj)

    def nonnull(self, value):
        if value is None: self.fail('System.NullReferenceException', 'Object reference not set to an instance of an object.')
        return value

    def raise_value(self, value):
        self.nonnull(value)
        if not self.is_type(value, 'System.Exception'): raise TypeError('throw requires a managed exception object')
        raise CliError(value)

    @staticmethod
    def truth(value):
        return value is not None and (value != 0 if isinstance(value, (int, float)) else True)

    @staticmethod
    def default(type_name):
        if type_name in ('System.Double', 'System.Single'): return 0.0
        if type_name in ('System.Boolean', 'System.Char', 'System.SByte', 'System.Byte', 'System.Int16',
                         'System.UInt16', 'System.Int32', 'System.UInt32', 'System.Int64', 'System.UInt64'): return 0
        return None

    def coerce(self, value, type_name):
        widths = {'System.SByte': (8, False), 'System.Byte': (8, True), 'System.Int16': (16, False),
                  'System.UInt16': (16, True), 'System.Char': (16, True), 'System.Int32': (32, False),
                  'System.UInt32': (32, True), 'System.Int64': (64, False), 'System.UInt64': (64, True)}
        if type_name in widths: return self.bits(value, *widths[type_name])
        if type_name == 'System.Boolean': return int(bool(value))
        if type_name == 'System.Double': return float(value)
        if type_name == 'System.String' and isinstance(value, str): return self.string(value)
        if type_name.endswith('[]') and isinstance(value, list):
            return CliArray(type_name[:-2], [self.coerce(v, type_name[:-2]) for v in value])
        return value

    def binary(self, op, left, right, kind):
        operation = op.split('.')[0]
        if kind == 'f':
            if operation == 'add': return left + right
            if operation == 'sub': return left - right
            if operation == 'mul': return left * right
            if operation == 'div':
                if right == 0.0:
                    if left == 0.0 or math.isnan(left): return float('nan')
                    return math.copysign(float('inf'), math.copysign(1.0, left) * math.copysign(1.0, right))
                return left / right
            if operation == 'rem':
                if right == 0.0 or math.isinf(left): return float('nan')
                return math.fmod(left, right)
            raise RuntimeError('Invalid floating operation: ' + op)
        width = 64 if kind == 'i8' else 32
        unsigned = op.endswith('.un')
        a, b = self.bits(left, width, unsigned), self.bits(right, width, unsigned)
        if operation == 'add': value = a + b
        elif operation == 'sub': value = a - b
        elif operation == 'mul': value = a * b
        elif operation in ('div', 'rem'):
            if b == 0: self.fail('System.DivideByZeroException', 'Attempted to divide by zero.')
            # portable-mvp selects CoreCLR x64's documented overflow behavior for rem as well as div.
            if not unsigned and a == -(1 << (width - 1)) and b == -1:
                self.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.')
            q = (abs(a) // abs(b)) * (-1 if (a < 0) != (b < 0) else 1)
            value = q if operation == 'div' else a - q * b
        elif operation == 'and': value = a & b
        elif operation == 'or': value = a | b
        elif operation == 'xor': value = a ^ b
        elif operation == 'shl': value = a << (int(right) & (width - 1))
        elif operation == 'shr': value = a >> (int(right) & (width - 1))
        else: raise RuntimeError('Invalid integer operation: ' + op)
        if '.ovf' in op:
            lo, hi = (0, (1 << width) - 1) if unsigned else (-(1 << (width - 1)), (1 << (width - 1)) - 1)
            if not lo <= value <= hi: self.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.')
        return self.bits(value, width)

    def unary(self, op, value, kind):
        if kind == 'f': return -value
        return self.bits(-value if op == 'neg' else ~value, 64 if kind == 'i8' else 32)

    def compare(self, op, left, right, kind):
        name = op.replace('.un', '')
        if kind == 'o':
            equal = left is right
            if name in ('ceq', 'beq'): return int(equal)
            if name == 'bne': return int(not equal)
            if op == 'cgt.un': return int(left is not None and right is None)
            raise RuntimeError('Unsupported ordered object comparison')
        if kind in ('i4', 'i8'):
            width = 64 if kind == 'i8' else 32
            left, right = self.bits(left, width, op.endswith('.un')), self.bits(right, width, op.endswith('.un'))
        elif math.isnan(left) or math.isnan(right):
            return int(op.endswith('.un'))
        if name in ('ceq', 'beq'): return int(left == right)
        if name == 'bne': return int(left != right)
        if name in ('cgt', 'bgt'): return int(left > right)
        if name in ('clt', 'blt'): return int(left < right)
        if name == 'bge': return int(left >= right)
        if name == 'ble': return int(left <= right)
        raise RuntimeError('Invalid comparison: ' + op)

    def convert(self, op, value, source):
        target = op.removeprefix('conv.').removeprefix('ovf.').removesuffix('.un')
        unsigned_source = op.endswith('.un') or (target == 'u8' and source == 'i4' and '.ovf' not in op)
        if source in ('i4', 'i8'): value = self.bits(value, 64 if source == 'i8' else 32, unsigned_source)
        if target in ('r8', 'r'): return float(value)
        width = {'1': 8, '2': 16, '4': 32, '8': 64}[target[-1]]
        unsigned = target[0] == 'u'
        if isinstance(value, float):
            if not math.isfinite(value):
                if '.ovf' in op: self.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.')
                raise RuntimeError('Unspecified CLI floating-to-integer conversion is outside portable-mvp')
            value = math.trunc(value)
        if '.ovf' in op:
            lo, hi = (0, (1 << width) - 1) if unsigned else (-(1 << (width - 1)), (1 << (width - 1)) - 1)
            if not lo <= value <= hi: self.fail('System.OverflowException', 'Arithmetic operation resulted in an overflow.')
        return self.bits(value, width, unsigned)

    def is_type(self, value, target):
        if value is None: return False
        if target == 'System.Object': return True
        if isinstance(value, CliString): return target == 'System.String'
        if isinstance(value, CliBox): return target in (value.type, 'System.ValueType')
        if isinstance(value, CliArray):
            if target == 'System.Array': return True
            if not target.endswith('[]'): return False
            want = target[:-2]
            if value.element == want: return True
            if self.default(value.element) is not None or self.default(want) is not None: return False
            return self.assignable(value.element, want)
        return isinstance(value, CliObject) and self.assignable(value.type, target)

    def assignable(self, actual, target):
        seen = set()
        while actual is not None and actual not in seen:
            if actual == target: return True
            seen.add(actual)
            actual = self.parents.get(actual)
        return False

    def cast(self, value, target, test=False):
        if value is None or self.is_type(value, target): return value
        if test: return None
        self.fail('System.InvalidCastException', 'Specified cast is not valid.')

    def box(self, value, type_name):
        return CliBox(type_name, self.coerce(value, type_name)) if self.default(type_name) is not None else value

    def unbox(self, value, type_name, address=False):
        if self.default(type_name) is None and not address: return self.cast(value, type_name)
        self.nonnull(value)
        if not isinstance(value, CliBox) or value.type != type_name: self.fail('System.InvalidCastException', 'Specified cast is not valid.')
        return CliRef(lambda: value.value, lambda v: setattr(value, 'value', self.coerce(v, type_name))) if address else value.value

    def cell_ref(self, cells, key, type_name):
        return CliRef(lambda: cells[key], lambda v: cells.__setitem__(key, self.coerce(v, type_name)))

    def load_ref(self, reference, type_name): return self.coerce(self.nonnull(reference).get(), type_name)
    def store_ref(self, reference, value, type_name): self.nonnull(reference).set(self.coerce(value, type_name))

    def array(self, type_name, length):
        if length < 0 or length > 2147483647: self.fail('System.OverflowException', 'Array dimensions exceeded supported range.')
        try: return CliArray(type_name, [self.default(type_name) for _ in range(length)])
        except (MemoryError, OverflowError): self.fail('System.OutOfMemoryException', 'Insufficient memory.')

    def _index(self, array, index):
        self.nonnull(array)
        if index < 0 or index >= len(array.data): self.fail('System.IndexOutOfRangeException', 'Index was outside the bounds of the array.')
        return index

    def array_length(self, array): return len(self.nonnull(array).data)
    def array_get(self, array, index, type_name):
        self._index(array, index)
        return self.coerce(array.data[index], type_name)
    def array_set(self, array, index, value, type_name):
        self._index(array, index)
        if self.default(array.element) is None and value is not None and not self.is_type(value, array.element):
            self.fail('System.ArrayTypeMismatchException', 'Attempted to access an element as a type incompatible with the array.')
        array.data[index] = self.coerce(value, array.element)

    def array_ref(self, array, index, type_name):
        self._index(array, index)
        if array.element != type_name: self.fail('System.ArrayTypeMismatchException', 'Array element reference type mismatch.')
        return self.cell_ref(array.data, index, type_name)

    def ensure(self, type_name):
        type_data = self.meta['types'].get(type_name)
        if not type_data or not type_data['cctor']: return
        state = self.initialization.get(type_name)
        if isinstance(state, CliError): raise state
        if state is not None: return
        self.initialization[type_name] = 'running'
        try:
            self.call(type_data['cctor'], [], False)
            self.initialization[type_name] = 'complete'
        except CliError as error:
            obj = CliObject('System.TypeInitializationException')
            obj.fields['$message'] = "The type initializer for '" + type_name + "' threw an exception."
            obj.fields['$inner'] = error.value
            wrapped = CliError(obj)
            self.initialization[type_name] = wrapped
            raise wrapped

    def field_get(self, obj, key):
        f = self.meta['fields'][key]
        if f['static']:
            self.ensure(f['owner']); return self.statics[key]
        return self.nonnull(obj).fields[key]

    def field_set(self, obj, key, value):
        f = self.meta['fields'][key]
        if f['static']:
            self.ensure(f['owner']); self.statics[key] = self.coerce(value, f['type'])
        else: self.nonnull(obj).fields[key] = self.coerce(value, f['type'])

    def field_ref(self, obj, key):
        f = self.meta['fields'][key]
        if f['static']: self.ensure(f['owner'])
        else: self.nonnull(obj)
        return CliRef(lambda: self.field_get(obj, key), lambda v: self.field_set(obj, key, v))

    def new_object(self, method_id, args):
        method = self.meta['methods'][method_id]
        name = method['type']
        type_data = self.meta['types'].get(name)
        if type_data and not type_data['before']: self.ensure(name)
        obj = CliObject(name)
        current, seen = name, set()
        while current in self.meta['types'] and current not in seen:
            seen.add(current)
            t = self.meta['types'][current]
            for key in t['fields']:
                f = self.meta['fields'][key]
                if not f['static']: obj.fields[key] = self.default(f['type'])
            current = t['base']
        self.call(method_id, [obj] + args, False)
        return obj

    def call(self, method_id, args, virtual=False):
        method = self.meta['methods'][method_id]
        if virtual:
            receiver = self.nonnull(args[0])
            if method['virtual'] and isinstance(receiver, CliObject):
                table = self.meta['types'].get(receiver.type, {}).get('vtable', {})
                method_id = table.get(method['slot'], method_id)
                method = self.meta['methods'][method_id]
        type_data = self.meta['types'].get(method['type'])
        if type_data and not type_data['before'] and method['name'] != '.cctor' and (not method['instance'] or method['name'] == '.ctor'):
            self.ensure(method['type'])
        if method['intrinsic'] is not None: return self.external(method, args)
        return self.functions[method_id](args)

    @staticmethod
    def double_text(value):
        if math.isnan(value): return 'NaN'
        if math.isinf(value): return '-Infinity' if value < 0 else 'Infinity'
        if value == 0: return '-0' if math.copysign(1.0, value) < 0 else '0'
        text = repr(float(value)).lower()
        sign = '-' if text.startswith('-') else ''
        text = text.lstrip('-')
        mantissa, _, exponent = text.partition('e')
        whole, _, fraction = mantissa.partition('.')
        exp = int(exponent or 0) + len(whole) - 1
        digits = (whole + fraction).lstrip('0')
        if whole == '0':
            leading = len(fraction) - len(fraction.lstrip('0'))
            exp = int(exponent or 0) - leading - 1
        digits = digits.rstrip('0') or '0'
        if exp < -4 or exp >= 17:
            body = digits[0] + ('.' + digits[1:] if len(digits) > 1 else '')
            return sign + body + 'E' + ('+' if exp >= 0 else '-') + str(abs(exp)).zfill(2)
        pos = exp + 1
        if pos <= 0: return sign + '0.' + '0' * (-pos) + digits
        if pos >= len(digits): return sign + digits + '0' * (pos - len(digits))
        return sign + digits[:pos] + '.' + digits[pos:]

    def format(self, value, type_name='System.Object'):
        if isinstance(value, CliRef): value = value.get()
        if value is None: return ''
        if isinstance(value, CliString): return value.text
        if isinstance(value, CliBox): return self.format(value.value, value.type)
        if type_name == 'System.Boolean': return 'True' if value else 'False'
        if type_name == 'System.Char': return chr(int(value) & 65535)
        if type_name == 'System.Double': return self.double_text(value)
        if isinstance(value, CliArray): return value.element + '[]'
        if isinstance(value, CliObject): return value.type
        return str(self.coerce(value, type_name))

    def external(self, method, args):
        op, parameters = method['intrinsic'], method['params']
        if op.startswith('console.'):
            self.write(('' if not args else self.format(args[0], parameters[0])) + ('\n' if op == 'console.line' else ''))
            return None
        if op == 'object.ctor': return None
        if op in ('object.string', 'primitive.string'): return self.string(self.format(args[0], method['type']))
        if op == 'reference.equals': return int(args[0] is args[1])
        if op.startswith('string.'):
            if op in ('string.equals', 'string.not-equals'):
                equal = (args[0] is None and args[1] is None) or (args[0] is not None and args[1] is not None and args[0].text == args[1].text)
                return int(not equal if op == 'string.not-equals' else equal)
            if op == 'string.concat':
                converted = [a if isinstance(a, CliString) else self.string(self.format(a)) for a in args if a is not None]
                nonempty = [a for a in converted if a.text]
                return nonempty[0] if len(nonempty) == 1 else self.string(''.join(a.text for a in converted), intern=not nonempty)
            text = self.nonnull(args[0]).text.encode('utf-16-le', 'surrogatepass')
            length = len(text) // 2
            if op == 'string.length': return length
            start = args[1]
            if op == 'string.char':
                if start < 0 or start >= length: self.fail('System.IndexOutOfRangeException', 'Index was outside the bounds of the array.')
                return int.from_bytes(text[start * 2:start * 2 + 2], 'little')
            count = length - start if len(args) == 2 else args[2]
            if start < 0 or count < 0 or start > length - count: self.fail('System.ArgumentOutOfRangeException', 'Substring range is out of bounds.')
            if start == 0 and count == length: return args[0]
            if count == 0: return self.string('', intern=True)
            return self.string(text[start * 2:(start + count) * 2].decode('utf-16-le', 'surrogatepass'))
        if op == 'exception.ctor':
            args[0].fields['$message'] = self.format(args[1]) if len(args) > 1 and args[1] is not None else "Exception of type '" + args[0].type + "' was thrown."
            return None
        if op == 'exception.message': return self.string(self.nonnull(args[0]).fields.get('$message', ''))
        if op == 'double.isnan': return int(math.isnan(args[0]))
        if op == 'double.isinf': return int(math.isinf(args[0]))
        if op.startswith('math.'):
            a = self.coerce(args[0], parameters[0])
            if op == 'math.abs':
                if parameters[0] in ('System.Int32', 'System.Int64') and a == -(1 << (31 if parameters[0] == 'System.Int32' else 63)):
                    self.fail('System.OverflowException', 'Negating the minimum value of a twos complement number is invalid.')
                return abs(a)
            if op in ('math.min', 'math.max'):
                b = self.coerce(args[1], parameters[1])
                if isinstance(a, float) and (math.isnan(a) or math.isnan(b)): return float('nan')
                if a == b == 0 and isinstance(a, float):
                    negative = math.copysign(1.0, a) < 0 or math.copysign(1.0, b) < 0
                    positive = math.copysign(1.0, a) > 0 or math.copysign(1.0, b) > 0
                    return -0.0 if (op == 'math.min' and negative) or (op == 'math.max' and not positive) else 0.0
                return min(a, b) if op == 'math.min' else max(a, b)
            if op == 'math.sqrt': return math.sqrt(a) if a >= 0 else float('nan')
            if not math.isfinite(a) or a == 0: return a
            if op == 'math.floor': return float(math.floor(a))
            if op == 'math.ceiling': return math.copysign(0.0, a) if -1 < a < 0 else float(math.ceil(a))
            if op == 'math.truncate': return math.copysign(0.0, a) if -1 < a < 0 else float(math.trunc(a))
        raise RuntimeError('Unimplemented validated intrinsic: ' + str(op))

    def invoke_export(self, name, args):
        candidates = [(k, v) for k, v in self.meta['exports'].items() if k == name or k.split('(')[0] == name]
        if len(candidates) != 1: raise ValueError('Use an unambiguous exported method signature: ' + name)
        token = candidates[0][1]
        method = self.meta['methods'][token]
        if len(args) != len(method['params']): raise ValueError('Incorrect argument count')
        result = self.call(token, [self.coerce(v, t) for v, t in zip(args, method['params'])], False)
        if isinstance(result, CliString): return result.text
        if method['returns'] == 'System.Boolean': return bool(result)
        return result

    def main(self, args):
        entry = self.meta['entry']
        if entry is None: raise ValueError('This is a library; use invoke(name, args)')
        method = self.meta['methods'][entry]
        argv = [] if not method['params'] else [self.coerce(args, 'System.String[]')]
        return self.call(entry, argv, False) or 0
