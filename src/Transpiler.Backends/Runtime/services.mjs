// Host lifetime services. The JavaScript engine owns the heap; no collection or finalization timing is promised.
class CliWeak extends CliObject {
    constructor(type, target) {
        super(type);
        if (typeof WeakRef !== 'function') throw new Error('This host does not support WeakRef');
        this.reference = target === null ? null : new WeakRef(target);
    }
}
class HostedRuntime extends ManagedRuntime {
    constructor(metadata, write = null) {
        super(metadata, write);
        this.parents['System.ObjectDisposedException'] = 'System.InvalidOperationException';
        this.identityHashes = new WeakMap(); this.nextHash = 1;
        this.roots = new Map(); this.nextRoot = 1;
    }
    new_object(methodId, args) {
        const method = this.meta.methods[methodId];
        if (method.intrinsic === 'weak.ctor') return new CliWeak(method.type, args[0]);
        return super.new_object(methodId, args);
    }
    external(method, args) {
        const op = method.intrinsic;
        if (op === 'gc.keep-alive') {
            // An empty JS call is not a specified liveness barrier. WeakRef construction/deref
            // uses ECMAScript's kept-alive list until the end of the current synchronous job.
            if (args[0] !== null) {
                if (typeof WeakRef !== 'function') throw new Error('GC.KeepAlive requires host WeakRef support');
                this.keepAliveBoundary = new WeakRef(args[0]);
                this.keepAliveBoundary.deref();
            }
            return null;
        }
        if (op === 'object.identity-hash') {
            const value = args[0];
            if (value === null) return 0;
            if (!this.identityHashes.has(value)) {
                this.identityHashes.set(value, this.nextHash);
                this.nextHash = ((this.nextHash + 1) & 0x7fffffff) || 1;
            }
            return this.identityHashes.get(value);
        }
        if (op === 'weak.set') {
            this.nonnull(args[0]).reference = args[1] === null ? null : new WeakRef(args[1]);
            return null;
        }
        if (op === 'weak.get') {
            const value = this.nonnull(args[0]).reference?.deref() ?? null;
            this.nonnull(args[1]).set(value);
            return Number(value !== null);
        }
        return super.external(method, args);
    }
    retain(value) {
        if (!Number.isSafeInteger(this.nextRoot)) throw new RangeError('Root handle budget exhausted');
        const handle = this.nextRoot++;
        this.roots.set(handle, value); return handle;
    }
    dereference_root(handle) {
        if (!Number.isSafeInteger(handle) || !this.roots.has(handle)) throw new RangeError('Unknown or released root handle');
        return this.roots.get(handle);
    }
    release(handle) {
        if (!Number.isSafeInteger(handle)) throw new TypeError('Root handle must be an integer');
        return this.roots.delete(handle);
    }
    runtime_info() {
        return {heap: 'host-gc', weakReferences: typeof WeakRef === 'function', explicitRoots: this.roots.size,
            forcedCollection: false, managedFinalizers: false, pinning: false,
            scheduler: 'cooperative-fifo-single-thread'};
    }
}
