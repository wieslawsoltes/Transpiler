// Host lifetime and clock services. Native callbacks publish readiness, never execute managed code.
class HostClockService {
    constructor() {
        this.timers = new Map(); this.notifications = new Map(); this.waiters = new Set();
        this.nextId = 1; this.version = {}; this.failure = null;
        this.install(null);
    }
    install(adapter) {
        if (this.timers.size || this.waiters.size) throw new Error('Cannot replace a clock with live timers or waiters');
        adapter ??= {
            now: () => {
                if (typeof globalThis.performance?.now !== 'function') throw new Error('Install a monotonic host clock');
                return globalThis.performance.now();
            },
            schedule: (milliseconds, callback) => setTimeout(callback, Math.min(milliseconds, 2147483647)),
            cancel: handle => clearTimeout(handle)
        };
        if (!adapter || ['now', 'schedule', 'cancel'].some(name => typeof adapter[name] !== 'function'))
            throw new TypeError('Clock requires now(), schedule(milliseconds, callback), and cancel(handle)');
        this.adapter = adapter; this.lastNow = -Infinity; this.failure = null;
    }
    now() {
        const value = this.adapter.now();
        if (typeof value !== 'number' || !Number.isFinite(value) || value < this.lastNow)
            throw new Error('Host clock must return finite, monotonic milliseconds');
        this.lastNow = value; return value;
    }
    delay(value) {
        if (!Number.isInteger(value) || value < -1 || value > 4294967294) throw new RangeError('Invalid timer delay');
    }
    get(id) {
        const timer = this.timers.get(id);
        if (!timer) throw new RangeError('Unknown or disposed timer handle');
        return timer;
    }
    create(milliseconds, callback) {
        this.delay(milliseconds);
        if (callback == null) throw new TypeError('Timer callback is required');
        if (this.nextId > 2147483647) throw new RangeError('Timer handle budget exhausted');
        const timer = {id: this.nextId++, callback, native: null, generation: {}, deadline: null, ready: false, everQueued: false};
        this.timers.set(timer.id, timer);
        try { this.change(timer.id, milliseconds); }
        catch (error) { this.destroy(timer.id); throw error; }
        return timer.id;
    }
    cancelNative(timer) {
        timer.generation = {}; // Invalidate before cancel(), which may itself call a stale callback.
        const native = timer.native; timer.native = null;
        if (native?.armed) this.adapter.cancel(native.handle);
    }
    schedule(timer) {
        const generation = timer.generation = {};
        const pending = timer.native = {armed: false};
        let handle;
        try {
            handle = this.adapter.schedule(Math.max(0, timer.deadline - this.now()), () => {
                if (this.timers.get(timer.id) !== timer || timer.generation !== generation) return;
                timer.generation = {}; // A native notification is single-consumption.
                timer.native = null;
                try {
                    if (this.now() >= timer.deadline) timer.ready = timer.everQueued = true;
                    this.notifications.set(timer.id, timer);
                } catch (error) { this.failure = error; }
                this.signal();
            });
        } catch (error) { timer.native = null; throw error; }
        // A synchronous adapter notification is allowed, but never runs managed code inline.
        if (timer.native === pending) { pending.handle = handle; pending.armed = true; }
        else this.adapter.cancel(handle);
    }
    change(id, milliseconds) {
        this.delay(milliseconds);
        const timer = this.get(id);
        this.cancelNative(timer);
        // Changing a due time cannot retract a callback already queued for managed execution.
        if (!timer.ready) this.notifications.delete(id);
        timer.deadline = milliseconds === -1 ? null : this.now() + milliseconds;
        if (timer.deadline !== null) this.schedule(timer);
        this.signal();
    }
    destroy(id) {
        const timer = this.timers.get(id);
        if (!timer) return;
        this.timers.delete(id); this.notifications.delete(id); timer.callback = null;
        try { this.cancelNative(timer); } finally { this.signal(); }
    }
    takeReady() {
        if (this.failure) throw this.failure;
        // Bound a poll even when an adapter synchronously notifies before its deadline.
        for (let remaining = this.notifications.size; remaining > 0; --remaining) {
            const [id, timer] = this.notifications.entries().next().value;
            this.notifications.delete(id);
            if (timer.ready) {
                timer.ready = false;
                if (timer.native === null) timer.deadline = null;
                return timer.callback;
            }
            if (timer.deadline !== null && timer.native === null) this.schedule(timer);
        }
        return null;
    }
    signal() {
        this.version = {};
        const waiters = [...this.waiters]; this.waiters.clear();
        for (const wake of waiters) wake();
    }
    canWait() { return this.notifications.size > 0 || [...this.timers.values()].some(timer => timer.native !== null); }
    async wait(version, signal = null) {
        if (this.failure) throw this.failure;
        if (signal?.aborted) throw signal.reason ?? new Error('Host wait canceled');
        if (version !== this.version || this.notifications.size || !this.canWait()) return;
        await new Promise((resolve, reject) => {
            const finish = error => {
                this.waiters.delete(wake);
                signal?.removeEventListener('abort', abort);
                if (error !== null) reject(error); else resolve();
            };
            const wake = () => finish(null);
            const abort = () => finish(signal.reason ?? new Error('Host wait canceled'));
            this.waiters.add(wake);
            try { signal?.addEventListener('abort', abort, {once: true}); }
            catch (error) { finish(error); }
            if (signal?.aborted) abort();
        });
    }
    info() {
        return {clock: 'host-clock-v2', activeTimers: this.timers.size, readyTimers: this.notifications.size,
            hostWaiters: this.waiters.size, idlePolicy: 'timer-notification-or-explicit-yield'};
    }
}

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
        this.parents['System.TimeoutException'] = 'System.SystemException';
        this.parents['System.ObjectDisposedException'] = 'System.InvalidOperationException';
        this.identityHashes = new WeakMap(); this.nextHash = 1;
        this.roots = new Map(); this.nextRoot = 1;
        this.clock = new HostClockService();
    }
    set_clock(adapter) { this.clock.install(adapter); }
    new_object(methodId, args) {
        const method = this.meta.methods[methodId];
        if (method.intrinsic === 'weak.ctor') return new CliWeak(method.type, args[0]);
        return super.new_object(methodId, args);
    }
    external(method, args) {
        const op = method.intrinsic;
        if (op === 'clock.now') return this.clock.now();
        if (op === 'clock.create-wide') return this.clock.create(Number(args[0]), this.nonnull(args[1]));
        if (op === 'clock.change-wide') { this.clock.change(args[0], Number(args[1])); return null; }
        if (op === 'clock.create') return this.clock.create(args[0], this.nonnull(args[1]));
        if (op === 'clock.change') { this.clock.change(args[0], args[1]); return null; }
        if (op === 'clock.fired') return Number(this.clock.get(args[0]).everQueued);
        if (op === 'clock.destroy') { this.clock.destroy(args[0]); return null; }
        if (op === 'clock.take') return this.clock.takeReady();
        if (op === 'clock.signal') { this.clock.signal(); return null; }
        if (op === 'gc.keep-alive') {
            // WeakRef's kept-alive list supplies the synchronous-job liveness boundary.
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
        const customYield = options.yieldHost !== undefined;
        const yieldHost = options.yieldHost ?? (() => new Promise(resolve => setTimeout(resolve, 0)));
        if (typeof yieldHost !== 'function') throw new TypeError('yieldHost must be a function');
        const candidates = Object.entries(this.meta.exports).filter(([k]) => k === name || k.split('(')[0] === name);
        if (candidates.length !== 1) throw new Error('Use an unambiguous exported method signature');
        const resultType = this.meta.methods[candidates[0][1]].returns;
        const binding = this.meta.asyncBindings[resultType];
        const value = this.invoke_export(name, args);
        if (!binding) return value;
        const receiver = binding.valueType ? this.cell_ref([value], 0, resultType) : value;
        for (let step = 0; ; ++step) {
            const version = this.clock.version;
            options.signal?.throwIfAborted();
            if (this.call(binding.completed, [receiver])) {
                const cell = [this.call(binding.getAwaiter, [receiver])];
                const result = this.call(binding.getResult, [this.cell_ref(cell, 0, binding.awaiterType)]);
                if (result instanceof CliString) return result.text;
                return binding.resultType === 'System.Boolean' ? Boolean(result) : result;
            }
            if (step === maxSteps) throw new Error('Cooperative task exceeded the host pump step budget');
            const progressed = this.call(binding.pump, []);
            if (!progressed && !customYield && this.clock.canWait()) await this.clock.wait(version, options.signal);
            else await yieldHost();
        }
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
            scheduler: 'cooperative-fifo-single-thread', ...this.clock.info()};
    }
}
export function setClock(adapter = null) { R.set_clock(adapter); }
