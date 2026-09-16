# Explicit binary32 evaluation/storage and bounded little-endian CLI field data.
class CliDataHandle:
    __slots__ = ('data',)
    def __init__(self, data): self.data = data


class NumericRuntime(ValueRuntime):
    @staticmethod
    def f32(value):
        try: return _struct.unpack('<f', _struct.pack('<f', float(value)))[0]
        except OverflowError: return math.copysign(float('inf'), value)

    def coerce(self, value, type_name):
        return self.f32(value) if type_name == 'System.Single' else super().coerce(value, type_name)

    def binary(self, op, a, b, kind):
        return self.f32(super().binary(op, a, b, 'f')) if kind == 'f4' else super().binary(op, a, b, kind)

    def unary(self, op, value, kind):
        return self.f32(super().unary(op, value, 'f')) if kind == 'f4' else super().unary(op, value, kind)

    def convert(self, op, value, source):
        if op in ('conv.r4', 'conv.r4.from-unsigned'):
            if source in ('i4', 'i8'):
                integer = self.bits(value, 32 if source == 'i4' else 64, op.endswith('unsigned'))
                negative = integer < 0; magnitude = abs(integer)
                shift = max(0, magnitude.bit_length() - 24)
                rounded = magnitude >> shift
                if shift:
                    remainder = magnitude - (rounded << shift); halfway = 1 << (shift - 1)
                    if remainder > halfway or remainder == halfway and rounded & 1: rounded += 1
                result = float(rounded) * (2 ** shift)
                return -result if negative else result
            return self.f32(value)
        return super().convert(op, value, 'f' if source == 'f4' else source)

    def finite(self, value):
        if not math.isfinite(value): self.fail('System.ArithmeticException', 'Non-finite floating-point value.')
        return value

    def semantic_hash(self, value, type_name, dispatch=True):
        value = self.dereference(value)
        if type_name == 'System.Single' and not isinstance(value, CliBox):
            if value == 0: return 0
            if math.isnan(value): return 2139095040
            return _struct.unpack('<i', _struct.pack('<f', value))[0]
        return super().semantic_hash(value, type_name, dispatch)

    def format(self, value, type_name='System.Object'):
        value = self.dereference(value)
        if isinstance(value, CliBox): return self.format(value.value, value.type)
        if type_name != 'System.Single': return super().format(value, type_name)
        value = self.f32(value)
        if not math.isfinite(value) or value == 0: return self.double_text(value)
        # Shortest decimal that round-trips through binary32; invariant general-format exponent thresholds.
        for precision in range(1, 10):
            text = format(value, '.' + str(precision) + 'g')
            if self.f32(float(text)) == value: break
        negative = text.startswith('-'); mantissa, _, power = text.lstrip('-').lower().partition('e')
        whole, _, fraction = mantissa.partition('.')
        exponent = int(power or '0') + len(whole) - 1
        if whole == '0': exponent = int(power or '0') - (len(fraction) - len(fraction.lstrip('0'))) - 1
        digits = (whole + fraction).lstrip('0').rstrip('0') or '0'
        sign = '-' if negative else ''
        if exponent < -4 or exponent >= 9:
            return sign + digits[0] + ('.' + digits[1:] if len(digits) > 1 else '') + 'E' + ('+' if exponent >= 0 else '-') + str(abs(exponent)).zfill(2)
        position = exponent + 1
        if position <= 0: return sign + '0.' + '0' * -position + digits
        if position >= len(digits): return sign + digits + '0' * (position - len(digits))
        return sign + digits[:position] + '.' + digits[position:]

    def data_handle(self, key):
        data = self.meta['fields'][key]['data']
        if data is None: raise RuntimeError('Missing validated field data')
        return CliDataHandle(bytes(data))

    def initialize_data(self, array, handle):
        self.nonnull(array)
        if not isinstance(array, CliArray) or not isinstance(handle, CliDataHandle): self.fail('System.ArgumentException', 'Invalid array initialization arguments.')
        element = self.meta['types'].get(array.element, {}).get('enumType') or array.element
        encodings = {'System.Boolean':'B','System.SByte':'b','System.Byte':'B','System.Char':'H',
                     'System.Int16':'h','System.UInt16':'H','System.Int32':'i','System.UInt32':'I',
                     'System.Int64':'q','System.UInt64':'Q','System.Single':'f','System.Double':'d'}
        code = encodings.get(element)
        if code is None: self.fail('System.ArgumentException', 'Array initialization requires primitive or enum elements.')
        size = _struct.calcsize('<' + code)
        if len(array.data) > len(handle.data) // size: self.fail('System.ArgumentException', 'Field data is shorter than the array.')
        for index in range(len(array.data)):
            array.data[index] = self.coerce(_struct.unpack_from('<' + code, handle.data, index * size)[0], element)

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'array.initialize-data': self.initialize_data(args[0], args[1]); return None
        if op == 'bits.single-i4': return _struct.unpack('<i', _struct.pack('<f', args[0]))[0]
        if op == 'bits.i4-single': return _struct.unpack('<f', _struct.pack('<i', self.bits(args[0],32)))[0]
        if op == 'bits.double-i8': return _struct.unpack('<q', _struct.pack('<d', args[0]))[0]
        if op == 'bits.i8-double': return _struct.unpack('<d', _struct.pack('<q', self.bits(args[0],64)))[0]
        if op.startswith('float.'):
            value = args[0]; test = op[6:]
            return int({'IsFinite':lambda:math.isfinite(value),'IsNaN':lambda:math.isnan(value),
                        'IsInfinity':lambda:math.isinf(value),'IsPositiveInfinity':lambda:value == float('inf'),
                        'IsNegativeInfinity':lambda:value == -float('inf'),'IsNegative':lambda:math.copysign(1,value)<0}[test]())
        if op.startswith('mathf.'):
            name = op[6:]
            if name == 'copysign': return self.f32(math.copysign(args[0], args[1]))
            proxy = dict(method, intrinsic='math.' + name, params=['System.Double'] * len(args))
            return self.f32(super().external(proxy, args))
        return super().external(method, args)
