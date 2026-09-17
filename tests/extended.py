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
