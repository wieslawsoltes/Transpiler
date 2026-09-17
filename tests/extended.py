"""Register independent compiler/runtime conformance batches."""

def register(h):
    import integration_batch
    import filter_batch
    integration_batch.register(h)
    filter_batch.register(h)
    import identity_batch
    identity_batch.register(h)
    import safety_batch
    safety_batch.register(h)
    import ssa_batch
    ssa_batch.register(h)
    import native_batch
    native_batch.register(h)
    import timer_batch
    timer_batch.register(h)
    import time_batch
    time_batch.register(h)
