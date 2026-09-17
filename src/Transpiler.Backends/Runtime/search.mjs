// Live managed activations: filter search precedes finally/fault unwinding.
class CliSearchFlow extends CliFlow {
    constructor(runtime, clauses) { super(runtime, clauses); this.pc = 0; this.evaluate = null; }
    finish(target, error, catcher, stack) {
        if (catcher !== null) this.runtime.searchPlans.delete(error);
        return super.finish(target, error, catcher, stack);
    }
    handle(error, pc, stack) {
        this.pc = pc;
        const [selected, clause] = this.runtime.search(error);
        const catcher = selected === this ? clause : null, target = catcher?.handlerStart ?? null;
        this.pending = this.pending.filter(p => target !== null && p.active.handlerStart <= target && target < p.active.handlerEnd);
        const handlers = this.clauses.filter(c => ['Finally','Fault'].includes(c.kind) && c.tryStart <= pc && pc < c.tryEnd &&
            (target === null || !(c.tryStart <= target && target < c.tryEnd)));
        handlers.sort((a,b) => (a.tryEnd-a.tryStart) - (b.tryEnd-b.tryStart));
        return this.begin(handlers, target, error, catcher, stack);
    }
}
class SearchRuntime extends StreamRuntime {
    constructor(metadata, write = null) {
        super(metadata, write); this.frames = []; this.searchBoundaries = []; this.searchPlans = new Map();
    }
    push_frame(frame) { this.frames.push(frame); }
    pop_frame(frame) {
        if (this.frames.at(-1) !== frame) throw new Error('Managed frame stack corrupted');
        this.frames.pop();
        for (const [error, plan] of this.searchPlans) if (plan[2] >= this.frames.length) this.searchPlans.delete(error);
        frame.evaluate = null;
    }
    search(error) {
        if (this.searchPlans.has(error)) return this.searchPlans.get(error);
        const floor = this.searchBoundaries.at(-1) ?? 0;
        for (let n = this.frames.length - 1; n >= floor; n--) {
            const frame = this.frames[n];
            const candidates = frame.clauses.filter(c => ['Catch','Filter'].includes(c.kind) && c.tryStart <= frame.pc && frame.pc < c.tryEnd);
            candidates.sort((a,b) => (a.tryEnd-a.tryStart) - (b.tryEnd-b.tryStart));
            for (const clause of candidates) {
                let accepted;
                if (clause.kind === 'Catch') accepted = this.is_type(error.value, clause.catchType);
                else {
                    this.searchBoundaries.push(this.frames.length);
                    try {
                        try { accepted = frame.evaluate(clause.filterStart, error.value) === 1; }
                        catch (inner) { if (!(inner instanceof CliError)) throw inner; accepted = false; }
                    } finally { this.searchBoundaries.pop(); }
                }
                if (accepted) {
                    const plan = [frame, clause, floor]; this.searchPlans.set(error, plan); return plan;
                }
            }
        }
        const plan = [null, null, floor]; this.searchPlans.set(error, plan); return plan;
    }
    ensure(type) {
        if (!this.frames) return super.ensure(type);
        this.searchBoundaries.push(this.frames.length);
        try { return super.ensure(type); } finally { this.searchBoundaries.pop(); }
    }
    runtime_info() { return {...super.runtime_info(), exceptionPolicy:'two-pass-managed-v1', activeFrames:this.frames.length}; }
}
