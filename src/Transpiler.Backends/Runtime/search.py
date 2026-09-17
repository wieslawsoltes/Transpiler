# Live managed activation search. Filters execute before any selected-frame unwinding.
class CliSearchFlow(CliFlow):
    def __init__(self, runtime, clauses):
        super().__init__(runtime, clauses)
        self.pc = 0
        self.evaluate = None

    def _finish(self, target, error, catcher, stack):
        if catcher is not None:
            self.runtime.search_plans.pop(error, None)
        return super()._finish(target, error, catcher, stack)

    def handle(self, error, pc, stack):
        self.pc = pc
        selected, clause, _ = self.runtime.search(error)
        catcher = clause if selected is self else None
        target = catcher['handlerStart'] if catcher is not None else None
        self.pending[:] = [p for p in self.pending if target is not None and
                           p['active']['handlerStart'] <= target < p['active']['handlerEnd']]
        handlers = [c for c in self.clauses if c['kind'] in ('Finally', 'Fault') and
                    c['tryStart'] <= pc < c['tryEnd'] and
                    (target is None or not c['tryStart'] <= target < c['tryEnd'])]
        handlers.sort(key=lambda c: c['tryEnd'] - c['tryStart'])
        return self._begin(handlers, target, error, catcher, stack)


class SearchRuntime(StreamRuntime):
    def __init__(self, metadata, write=None):
        super().__init__(metadata, write)
        self.frames, self.search_boundaries, self.search_plans = [], [], {}

    def push_frame(self, frame): self.frames.append(frame)

    def pop_frame(self, frame):
        if not self.frames or self.frames[-1] is not frame: raise RuntimeError('Managed frame stack corrupted')
        self.frames.pop()
        # An exception escaping its search domain cannot retain obsolete activation references.
        for error, plan in list(self.search_plans.items()):
            if plan[2] >= len(self.frames): del self.search_plans[error]
        frame.evaluate = None

    def search(self, error):
        if error in self.search_plans: return self.search_plans[error]
        floor = self.search_boundaries[-1] if self.search_boundaries else 0
        for frame in reversed(self.frames[floor:]):
            candidates = [c for c in frame.clauses if c['kind'] in ('Catch', 'Filter') and
                          c['tryStart'] <= frame.pc < c['tryEnd']]
            # Stable ordering preserves metadata order for sibling handlers.
            candidates.sort(key=lambda c: c['tryEnd'] - c['tryStart'])
            for clause in candidates:
                if clause['kind'] == 'Catch': accepted = self.is_type(error.value, clause['catchType'])
                else:
                    # Any new exception may search helper frames, not the suspended original call stack.
                    self.search_boundaries.append(len(self.frames))
                    try:
                        try: accepted = frame.evaluate(clause['filterStart'], error.value) == 1
                        except CliError: accepted = False
                    finally: self.search_boundaries.pop()
                if accepted:
                    plan = (frame, clause, floor)
                    self.search_plans[error] = plan
                    return plan
        plan = (None, None, floor)
        self.search_plans[error] = plan
        return plan

    def ensure(self, type_name):
        # The runtime catches initializer failures and publishes a TypeInitializationException.
        # Caller filters must see that wrapper, never the intercepted initializer exception.
        if not hasattr(self, 'frames'): return super().ensure(type_name)
        self.search_boundaries.append(len(self.frames))
        try: return super().ensure(type_name)
        finally: self.search_boundaries.pop()

    def runtime_info(self):
        return dict(super().runtime_info(), exceptionPolicy='two-pass-managed-v1', activeFrames=len(self.frames))
