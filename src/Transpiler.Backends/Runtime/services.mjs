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
    async await_export(name, args, options = {}) {
        const maxSteps = options.maxSteps ?? 100000;
        if (!Number.isSafeInteger(maxSteps) || maxSteps < 1) throw new TypeError('maxSteps must be a positive integer');
        const yieldHost = options.yieldHost ?? (() => new Promise(resolve => setTimeout(resolve, 0)));
        if (typeof yieldHost !== 'function') throw new TypeError('yieldHost must be a function');
        const candidates = Object.entries(this.meta.exports).filter(([k]) => k === name || k.split('(')[0] === name);
        if (candidates.length !== 1) throw new Error('Use an unambiguous exported method signature');
        const resultType = this.meta.methods[candidates[0][1]].returns;
        const binding = this.meta.asyncBindings[resultType];
        const value = this.invoke_export(name, args);
        if (!binding) return value;
        const receiver = binding.valueType ? this.cell_ref([value], 0, resultType) : value;
        for (let step = 0; step < maxSteps; ++step) {
            options.signal?.throwIfAborted();
            if (this.call(binding.completed, [receiver])) {
                const cell = [this.call(binding.getAwaiter, [receiver])];
                const result = this.call(binding.getResult, [this.cell_ref(cell, 0, binding.awaiterType)]);
                if (result instanceof CliString) return result.text;
                return binding.resultType === 'System.Boolean' ? Boolean(result) : result;
            }
            this.call(binding.pump, []);
            await yieldHost();
        }
        throw new Error('Cooperative task exceeded the host pump step budget');
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
