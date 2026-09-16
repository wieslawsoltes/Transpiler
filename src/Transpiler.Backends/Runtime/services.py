# Host lifetime services. Python owns the heap; this is not a second tracing collector.
import weakref as _weakref


class CliWeak(CliObject):
    __slots__ = ('reference',)
    def __init__(self, type_name, target):
        super().__init__(type_name)
        self.reference = None if target is None else _weakref.ref(target)


class HostedRuntime(ManagedRuntime):
    def __init__(self, metadata, write=None):
        super().__init__(metadata, write)
        self.parents['System.ObjectDisposedException'] = 'System.InvalidOperationException'
        self._identity_hashes = _weakref.WeakKeyDictionary()
        self._next_hash = 1
        self._roots, self._next_root = {}, 1

    def new_object(self, method_id, args):
        method = self.meta['methods'][method_id]
        if method['intrinsic'] == 'weak.ctor': return CliWeak(method['type'], args[0])
        return super().new_object(method_id, args)

    def external(self, method, args):
        op = method['intrinsic']
        if op == 'gc.keep-alive':
            # The actual argument remains a strong Python reference through this call boundary.
            return None
        if op == 'object.identity-hash':
            value = args[0]
            if value is None: return 0
            if value not in self._identity_hashes:
                self._identity_hashes[value] = self._next_hash
                self._next_hash = (self._next_hash + 1) & 0x7fffffff or 1
            return self._identity_hashes[value]
        if op == 'weak.set':
            self.nonnull(args[0]).reference = None if args[1] is None else _weakref.ref(args[1])
            return None
        if op == 'weak.get':
            reference = self.nonnull(args[0]).reference
            value = None if reference is None else reference()
            self.nonnull(args[1]).set(value)
            return int(value is not None)
        return super().external(method, args)

    async def await_export(self, name, args, max_steps=100000):
        import asyncio
        if not isinstance(max_steps, int) or max_steps < 1: raise ValueError('max_steps must be a positive integer')
        candidates = [(k, v) for k, v in self.meta['exports'].items() if k == name or k.split('(')[0] == name]
        if len(candidates) != 1: raise ValueError('Use an unambiguous exported method signature')
        result_type = self.meta['methods'][candidates[0][1]]['returns']
        binding = self.meta['asyncBindings'].get(result_type)
        value = self.invoke_export(name, args)
        if binding is None: return value
        receiver = self.cell_ref([value], 0, result_type) if binding.get('valueType') else value
        for _ in range(max_steps):
            if self.call(binding['completed'], [receiver]):
                cell = [self.call(binding['getAwaiter'], [receiver])]
                result = self.call(binding['getResult'], [self.cell_ref(cell, 0, binding['awaiterType'])])
                if isinstance(result, CliString): return result.text
                return bool(result) if binding['resultType'] == 'System.Boolean' else result
            self.call(binding['pump'], [])
            await asyncio.sleep(0)
        raise TimeoutError('Cooperative task exceeded the host pump step budget')

    def retain(self, value):
        if self._next_root > 9007199254740991: raise OverflowError('Root handle budget exhausted')
        handle = self._next_root
        self._next_root += 1
        self._roots[handle] = value
        return handle

    def dereference_root(self, handle):
        if type(handle) is not int or handle not in self._roots: raise ValueError('Unknown or released root handle')
        return self._roots[handle]

    def release(self, handle):
        if type(handle) is not int: raise ValueError('Root handle must be an integer')
        if handle not in self._roots: return False
        del self._roots[handle]
        return True

    def runtime_info(self):
        return {'heap': 'host-gc', 'weakReferences': True, 'explicitRoots': len(self._roots),
                'forcedCollection': False, 'managedFinalizers': False, 'pinning': False,
                'scheduler': 'cooperative-fifo-single-thread'}
