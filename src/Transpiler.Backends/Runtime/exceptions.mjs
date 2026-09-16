// Selected Exception virtual contracts; aggregate algorithms are translated C#.
class ExceptionRuntime extends NumericRuntime {
    call(methodId, args, virtual = false, constrained = null) {
        const method = this.meta.methods[methodId];
        const name = {'exception.message':'get_Message', 'exception.base':'GetBaseException'}[method.intrinsic];
        if (virtual && name) {
            const slot = this.object_slot(this.nonnull(args[0]), name);
            if (slot) return this.invoke_slot(slot, args[0], args.slice(1));
        }
        return super.call(methodId, args, virtual, constrained);
    }
    external(method, args) {
        const op = method.intrinsic;
        if (op === 'exception.ctor') {
            const result = super.external(method, args);
            args[0].fields.$inner = args.length > 2 ? args[2] : null;
            return result;
        }
        if (op === 'exception.inner') return this.nonnull(args[0]).fields.$inner ?? null;
        if (op === 'exception.base') {
            let value = this.nonnull(args[0]);
            while (value.fields.$inner != null) value = value.fields.$inner;
            return value;
        }
        return super.external(method, args);
    }
}
