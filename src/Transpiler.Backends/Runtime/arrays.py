# CLI array shapes and opaque type identity. Method/field reflection is a separate capability.
class CliTypeHandle:
    __slots__ = ('name',)
    def __init__(self, name): self.name = name


class CliType(CliObject):
    __slots__ = ('name',)
    def __init__(self, name): super().__init__('System.RuntimeType'); self.name = name


class CliRectArray(CliArray):
    __slots__ = ('lengths', 'lower', 'type')
    def __init__(self, element, data, lengths, lower):
        super().__init__(element, data)
        self.lengths, self.lower = tuple(lengths), tuple(lower)
        self.type = element + '[rank=' + str(len(lengths)) + ']'


class ArrayRuntime(ExceptionRuntime):
    def __init__(self, metadata, write=None):
        super().__init__(metadata, write)
        self.parents['System.TypeLoadException'] = 'System.SystemException'
        self._type_cache = {}

    @staticmethod
    def shape(name):
        if name.endswith('[]'): return name[:-2], 1, True
        at = name.rfind('[rank=')
        if at >= 0 and name.endswith(']'): return name[:at], int(name[at+6:-1]), False
        return None

    def type_handle(self, name): return CliTypeHandle(name)
    def type_object(self, name):
        if name not in self._type_cache: self._type_cache[name] = CliType(name)
        return self._type_cache[name]

    def actual_type(self, value, fallback='System.Object'):
        return value.type if isinstance(value, CliRectArray) else super().actual_type(value, fallback)

    def format(self, value, type_name='System.Object'):
        if isinstance(value, CliType): return self.type_text(value.name)
        if isinstance(value, CliRectArray): return self.type_text(value.type)
        return super().format(value, type_name)

    def type_text(self, name):
        shape = self.shape(name)
        if shape: return self.type_text(shape[0]) + ('[]' if shape[2] else '[*]' if shape[1] == 1 else '[' + ',' * (shape[1]-1) + ']')
        if name.startswith('['): name = name[name.index(']')+1:]
        # ToString representation, not assembly-qualified FullName reflection.
        return name.replace('<', '[').replace('>', ']')

    def is_type(self, value, target):
        if isinstance(value, CliType): return target in ('System.Object', 'System.Type', 'System.Reflection.MemberInfo', 'System.RuntimeType')
        if isinstance(value, CliRectArray):
            if target in ('System.Object', 'System.Array', 'System.Collections.IEnumerable', 'System.ICloneable'): return True
            shape = self.shape(target)
            if not shape or shape[2] or shape[1] != len(value.lengths): return False
            return value.element == shape[0] or self.default(value.element) is None and self.default(shape[0]) is None and self.assignable(value.element, shape[0])
        return super().is_type(value, target)

    def rect_array(self, element, lengths, lower=None, vector=False, creation=False):
        lower = [0] * len(lengths) if lower is None else list(lower)
        if len(lengths) > 32: self.fail('System.TypeLoadException', 'Array rank exceeds 32.')
        if len(lengths) == 0 or len(lower) != len(lengths): self.fail('System.ArgumentException', 'Invalid array rank or lower bounds.')
        for length, lo in zip(lengths, lower):
            if length < 0: self.fail('System.ArgumentOutOfRangeException' if creation else 'System.OverflowException', 'Negative array length.')
            if lo + length - 1 > 2147483647: self.fail('System.ArgumentOutOfRangeException', 'Array upper bound exceeds Int32.')
        count = 0 if 0 in lengths else math.prod(lengths)
        if count > 2147483647: self.fail('System.OutOfMemoryException', 'Array exceeds the portable storage limit.')
        if element == 'System.Void' or element.endswith('&') or element.endswith('*'): self.fail('System.NotSupportedException', 'Invalid array element type.')
        array = self.array(element, count)
        return array if vector and len(lengths) == 1 and lower[0] == 0 else CliRectArray(element, array.data, lengths, lower)

    def array_shape(self, array):
        self.nonnull(array)
        return (array.lengths, array.lower) if isinstance(array, CliRectArray) else ([len(array.data)], [0])

    def flat_index(self, array, indices):
        lengths, lower = self.array_shape(array)
        if len(indices) != len(lengths): self.fail('System.ArgumentException', 'Index count does not match array rank.')
        index = 0
        for value, length, lo in zip(indices, lengths, lower):
            if value < -2147483648 or value > 2147483647: self.fail('System.ArgumentOutOfRangeException', 'Index exceeds Int32.')
            relative = value - lo
            if relative < 0 or relative >= length: self.fail('System.IndexOutOfRangeException', 'Array index out of bounds.')
            index = index * length + relative
        return index

    def new_object(self, method_id, args):
        method = self.meta['methods'][method_id]
        if method['intrinsic'] == 'rect.new':
            element, rank, _ = self.shape(method['type'])
            return self.rect_array(element, args if len(args) == rank else args[1::2], None if len(args) == rank else args[0::2])
        return super().new_object(method_id, args)

    def store_boxed(self, array, index, value):
        element = array.element
        if self.default(element) is None:
            if value is not None and not self.is_type(value, element): self.fail('System.InvalidCastException', 'Invalid array element type.')
            array.data[index] = value; return
        if value is None: array.data[index] = self.default(element); return
        nullable = self.nullable(element)
        if nullable or isinstance(value, CliBox) and value.type == element:
            array.data[index] = self.unbox(value, element); return
        widening = {'System.SByte':('System.Int16','System.Int32','System.Int64','System.Single','System.Double'),
            'System.Byte':('System.Char','System.Int16','System.UInt16','System.Int32','System.UInt32','System.Int64','System.UInt64','System.Single','System.Double'),
            'System.Char':('System.UInt16','System.Int32','System.UInt32','System.Int64','System.UInt64','System.Single','System.Double'),
            'System.Int16':('System.Int32','System.Int64','System.Single','System.Double'),
            'System.UInt16':('System.Char','System.Int32','System.UInt32','System.Int64','System.UInt64','System.Single','System.Double'),
            'System.Int32':('System.Int64','System.Single','System.Double'),
            'System.UInt32':('System.Int64','System.UInt64','System.Single','System.Double'),
            'System.Int64':('System.Single','System.Double'),'System.UInt64':('System.Single','System.Double'),'System.Single':('System.Double',)}
        if self.meta['types'].get(element, {}).get('valueType'):
            self.fail('System.InvalidCastException', 'Array value type requires an exact boxed match.')
        target = element
        source = self.meta['types'].get(getattr(value, 'type', ''), {}).get('enumType') or getattr(value, 'type', '')
        if not isinstance(value, CliBox) or target not in (source,) + widening.get(source, ()):
            numeric = source in widening or source in ('System.Boolean', 'System.Double')
            self.fail('System.ArgumentException' if numeric else 'System.InvalidCastException', 'Invalid array element conversion.')
        number = value.value
        if target == 'System.Single' and source in ('System.Int64','System.UInt64'):
            number = self.convert('conv.r4.from-unsigned' if source == 'System.UInt64' else 'conv.r4', number, 'i8')
        array.data[index] = self.coerce(number, element)

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'type.from-handle': return self.type_object(args[0].name)
        if op == 'type.of-object': return self.type_object(self.actual_type(self.nonnull(args[0])))
        if op in ('type.equals', 'type.not-equals'): return int((args[0] is args[1]) != (op == 'type.not-equals'))
        if op in ('type.element','type.rank','type.is-array'):
            shape = self.shape(self.nonnull(args[0]).name)
            if op == 'type.element': return self.type_object(shape[0]) if shape else None
            if op == 'type.is-array': return int(shape is not None)
            if not shape: self.fail('System.ArgumentException', 'Type is not an array.')
            return shape[1]
        if op and op.startswith('rect.'):
            array = self.nonnull(args[0]); indices = args[1:-1] if op == 'rect.set' else args[1:]
            index = self.flat_index(array, indices)
            if op == 'rect.get': return self.coerce(array.data[index], method['returns'])
            if op == 'rect.address':
                element = method['returns'][:-1]
                if element != array.element: self.fail('System.ArrayTypeMismatchException', 'Array address type mismatch.')
                return self.cell_ref(array.data, index, element)
            self.array_set(array, index, args[-1], array.element); return None
        if not op or not op.startswith('array.') or op == 'array.initialize-data': return super().external(method, args)
        if op.startswith('array.create'):
            if args[0] is None: self.fail('System.ArgumentNullException', 'Element type is null.')
            if not isinstance(args[0], CliType): self.fail('System.ArgumentException', 'Invalid element type.')
            if op != 'array.create' and args[1] is None or op == 'array.create-bounds' and args[2] is None:
                self.fail('System.ArgumentNullException', 'Array lengths or bounds are null.')
            lengths = args[1:] if op == 'array.create' else args[1].data
            lower = args[2].data if op == 'array.create-bounds' else None
            return self.rect_array(args[0].name, lengths, lower, vector=True, creation=True)
        if op in ('array.clear','array.clear-all') and args[0] is None: self.fail('System.ArgumentNullException','Array is null.')
        array = self.nonnull(args[0]); lengths, lower = self.array_shape(array)
        if op == 'array.Length': return len(array.data)
        if op == 'array.LongLength': return len(array.data)
        if op == 'array.Rank': return len(lengths)
        if op in ('array.GetLength','array.GetLongLength','array.GetLowerBound','array.GetUpperBound'):
            dimension = args[1]
            if dimension < 0 or dimension >= len(lengths): self.fail('System.IndexOutOfRangeException', 'Invalid dimension.')
            if op == 'array.GetLowerBound': return lower[dimension]
            if op == 'array.GetUpperBound': return self.bits(lower[dimension]+lengths[dimension]-1,32)
            return lengths[dimension]
        if op == 'array.clone':
            data = [self.copy_value(v) for v in array.data]
            return CliRectArray(array.element,data,lengths,lower) if isinstance(array,CliRectArray) else CliArray(array.element,data)
        if op in ('array.clear','array.clear-all'):
            start, count = (args[1]-lower[0], args[2]) if op == 'array.clear' else (0,len(array.data))
            if start < 0 or count < 0 or start > len(array.data)-count: self.fail('System.IndexOutOfRangeException','Invalid array range.')
            for i in range(start,start+count): array.data[i] = self.default(array.element)
            return None
        if op == 'array.enumerate':
            constructor = self.meta['arrayEnumerators'].get(array.element+'[]')
            if constructor is None: raise RuntimeError('Array enumerator was not rooted.')
            return self.new_object(constructor,[CliArray(array.element,array.data)])
        values = args[2:] if op == 'array.set-value' else args[1:]
        if method['params'][-1].endswith('[]'):
            if values[0] is None: self.fail('System.ArgumentNullException','Indices are null.')
            values = values[0].data
        elif method['params'][-1] == 'System.Int64':
            if any(v < -2147483648 or v > 2147483647 for v in values):
                self.fail('System.ArgumentOutOfRangeException', 'Index exceeds Int32.')
        index = self.flat_index(array,values)
        if op == 'array.get-value': return self.box(array.data[index],array.element)
        self.store_boxed(array,index,args[1]); return None
