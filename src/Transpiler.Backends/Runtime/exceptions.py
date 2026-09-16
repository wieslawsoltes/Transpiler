# Selected Exception virtual contracts; aggregate algorithms are translated C#.
class ExceptionRuntime(NumericRuntime):
    def call(self, method_id, args, virtual=False, constrained=None):
        method = self.meta['methods'][method_id]
        name = {'exception.message': 'get_Message', 'exception.base': 'GetBaseException'}.get(method['intrinsic'])
        if virtual and name:
            slot = self.object_slot(self.nonnull(args[0]), name)
            if slot: return self.invoke_slot(slot, args[0], args[1:])
        return super().call(method_id, args, virtual, constrained)

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'exception.ctor':
            result = super().external(method, args)
            args[0].fields['$inner'] = args[2] if len(args) > 2 else None
            return result
        if op == 'exception.inner': return self.nonnull(args[0]).fields.get('$inner')
        if op == 'exception.base':
            value = self.nonnull(args[0])
            while value.fields.get('$inner') is not None: value = value.fields['$inner']
            return value
        return super().external(method, args)
