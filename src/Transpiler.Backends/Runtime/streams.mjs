// Native async-iterator ownership adapter. All enumerator/ValueTask algorithms live in translated StreamCursor<T>.
export class StreamCleanupPendingError extends Error {
    constructor(phase, cause) {
        super('Stream cleanup is still pending (' + phase + '); complete the operation and retry return()', { cause });
        this.name = 'StreamCleanupPendingError'; this.phase = phase;
    }
}
function streamAbortError() { const error = new Error('Stream enumeration was canceled'); error.name = 'AbortError'; return error; }
class ManagedAsyncIterator {
    constructor(runtime, name, args, options) {
        if (!Array.isArray(args)) throw new TypeError('Stream arguments must be an array');
        if (options === null || typeof options !== 'object') throw new TypeError('Stream options must be an object');
        this.runtime = runtime; this.name = name; this.args = [...args];
        this.maxSteps = options.maxSteps ?? 100000; this.cleanupSteps = options.cleanupSteps ?? this.maxSteps;
        for (const n of [this.maxSteps, this.cleanupSteps]) if (!Number.isSafeInteger(n) || n < 1) throw new TypeError('Stream step budgets must be positive safe integers');
        this.yieldHost = options.yieldHost ?? (() => new Promise(resolve => setTimeout(resolve, 0)));
        if (typeof this.yieldHost !== 'function') throw new TypeError('yieldHost must be a function');
        this.signal = options.signal ?? null;
        if (this.signal && (typeof this.signal.aborted !== 'boolean' || typeof this.signal.addEventListener !== 'function' || typeof this.signal.removeEventListener !== 'function'))
            throw new TypeError('signal must implement AbortSignal');
        const entries = Object.entries(runtime.meta.exports).filter(([k]) => k === name || k.split('(')[0] === name);
        if (entries.length !== 1) throw new TypeError('Use an unambiguous exported stream signature: ' + name);
        const method = runtime.meta.methods[entries[0][1]];
        if (method.params.length !== args.length) throw new TypeError('Incorrect stream argument count');
        this.binding = runtime.meta.streamBindings[method.returns];
        if (!this.binding) throw new TypeError('Export requires a linked IAsyncEnumerable<T> stream contract');
        this.cursor = null; this.closed = false; this.stopping = false;
        this.busy = null; this.closing = null; this.hasReason = false; this.reason = undefined;
        this.cancelSent = false; this.cancelError = null; this.listener = null; this.callingYield = false;
    }
    [Symbol.asyncIterator]() { return this; }
    get pending() {
        if (!this.cursor) return null;
        return this.call('get_MovePending') ? 'move' : this.call('get_DisposePending') ? 'dispose' : null;
    }
    call(member) { return this.runtime.call(this.binding.methods[member], [this.cursor]); }
    requestCancel() {
        if (!this.cursor || this.cancelSent) return;
        this.cancelSent = true;
        try { this.call('Cancel'); } catch (error) { this.cancelError = error; }
    }
    cancel(reason = streamAbortError()) {
        if (this.closed || this.hasReason) return false;
        this.hasReason = true; this.reason = reason; this.requestCancel(); return true;
    }
    checkAbort() {
        if (this.signal?.aborted && !this.hasReason) this.cancel(this.signal.reason ?? streamAbortError());
        if (this.cancelError) throw this.cancelError;
        if (this.hasReason) throw this.reason;
    }
    open() {
        if (this.cursor) return;
        const enumerable = this.runtime.invoke_export(this.name, this.args);
        this.cursor = this.runtime.call(this.binding.methods.Open, [enumerable]);
        this.args = null; this.runtime.activeStreams++;
        if (this.signal) {
            this.listener = () => this.cancel(this.signal.reason ?? streamAbortError());
            this.signal.addEventListener('abort', this.listener, { once: true });
        }
        this.checkAbort();
    }
    detachSignal() {
        if (this.listener) this.signal.removeEventListener('abort', this.listener);
        this.listener = null;
    }
    observeClosed() {
        if (this.cursor && this.call('get_IsClosed')) { this.cursor = null; this.runtime.activeStreams--; }
        if (!this.cursor) {
            this.closed = true; this.args = null; this.detachSignal(); this.signal = null;
            this.yieldHost = null; // No enumeration-owned callback survives successful/failed terminal disposal.
        }
    }
    async wait(member, steps, cleanup) {
        for (let step = 0; ; step++) {
            if (!cleanup) this.checkAbort();
            if (this.call(member)) return;
            if (step === steps) throw new Error('Stream operation exceeded its pump step budget');
            this.runtime.call(this.binding.pump, []);
            let yielded;
            this.callingYield = true;
            try { yielded = this.yieldHost(); } finally { this.callingYield = false; }
            await yielded;
        }
    }
    async drainDispose() {
        this.stopping = true; this.detachSignal();
        if (!this.cursor) { this.observeClosed(); return; }
        try {
            if (this.call('get_MovePending')) {
                this.requestCancel();
                try { await this.wait('get_MoveCompleted', this.cleanupSteps, true); }
                catch (error) { throw new StreamCleanupPendingError('move', error); }
                // Closing discards a pending item or managed cancellation/fault, but consumes it once.
                try { this.call('FinishMove'); } catch (error) { if (!(error instanceof CliError)) throw error; }
            }
            this.call('StartDispose');
            try { await this.wait('get_DisposeCompleted', this.cleanupSteps, true); }
            catch (error) { throw new StreamCleanupPendingError('dispose', error); }
            this.call('FinishDispose');
            if (this.cancelError) throw this.cancelError;
        } finally { this.observeClosed(); }
    }
    async advance() {
        if (this.closed) return { done: true, value: undefined };
        if (this.stopping) {
            if (this.closing) return { done: true, value: undefined };
            throw new TypeError('Stream cleanup must finish before further iteration');
        }
        try {
            this.checkAbort(); this.open(); this.call('StartMove');
            await this.wait('get_MoveCompleted', this.maxSteps, false);
            this.checkAbort();
            const more = this.call('FinishMove');
            if (!more || this.stopping) { await this.drainDispose(); return { done: true, value: undefined }; }
            const value = this.call('get_Current');
            return { done: false, value: this.runtime.stream_value(value, this.binding.element) };
        } catch (error) {
            if (this.stopping) throw error; // Do not silently double the configured cleanup budget.
            try { await this.drainDispose(); }
            catch (cleanup) { if (cleanup instanceof StreamCleanupPendingError) cleanup.operationError = error; throw cleanup; }
            throw error;
        }
    }
    next() {
        if (this.busy) return Promise.reject(new TypeError('Overlapping stream next() calls are not allowed'));
        const operation = Promise.resolve().then(() => this.advance()).finally(() => { if (this.busy === operation) this.busy = null; });
        this.busy = operation; return operation;
    }
    return(value) {
        if (this.callingYield) return Promise.reject(new TypeError('Cannot close reentrantly from the stream yield callback'));
        this.stopping = true;
        if (this.busy) this.requestCancel();
        if (!this.closing) {
            const previous = this.busy;
            const operation = Promise.resolve().then(async () => {
                if (previous) { try { await previous; } catch { /* advance performs its own drain/cleanup */ } }
                await this.drainDispose();
            }).finally(() => { if (this.closing === operation) this.closing = null; });
            this.closing = operation;
        }
        return this.closing.then(() => ({ done: true, value }));
    }
    async throw(error) { await this.return(); throw error; }
}
if (typeof Symbol.asyncDispose === 'symbol')
    ManagedAsyncIterator.prototype[Symbol.asyncDispose] = async function () { await this.return(); };
class StreamRuntime extends ArrayRuntime {
    constructor(metadata, write = null) { super(metadata, write); this.activeStreams = 0; }
    stream(name, args = [], options = {}) { return new ManagedAsyncIterator(this, name, args, options); }
    stream_value(value, type) {
        if (value instanceof CliString) return value.text;
        if (value instanceof CliNaN) return Number(value);
        return type === 'System.Boolean' ? Boolean(value) : value;
    }
    runtime_info() { return { ...super.runtime_info(), activeStreams: this.activeStreams, streamPolicy: 'managed-stream-v1' }; }
}
