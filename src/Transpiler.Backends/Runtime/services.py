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
