using System;
using System.Collections.Generic;

namespace Transpiler.Bcl;

/// <summary>Snapshot-based aggregation. Awaiting a Task still throws its original first exception.</summary>
public class AggregateException : Exception
{
    private const string DefaultMessage = "One or more errors occurred.";
    private readonly ReadOnlyCollection<Exception> _inner;
    public AggregateException() : this(DefaultMessage, new Exception[0]) { }
    public AggregateException(string? message) : this(message, new Exception[0]) { }
    public AggregateException(string? message, Exception innerException) : this(message, new[] { innerException }) { }
    public AggregateException(IEnumerable<Exception> innerExceptions) : this(DefaultMessage, innerExceptions) { }
    public AggregateException(params Exception[] innerExceptions) : this(DefaultMessage, (IEnumerable<Exception>)innerExceptions) { }
    public AggregateException(string? message, params Exception[] innerExceptions) : this(message, (IEnumerable<Exception>)innerExceptions) { }
    public AggregateException(string? message, IEnumerable<Exception> innerExceptions) : this(message, Snapshot(innerExceptions)) { }
    private AggregateException(string? message, List<Exception> inner) : base(message, inner.Count == 0 ? null : inner[0])
    { _inner = new ReadOnlyCollection<Exception>(inner); }
    private static List<Exception> Snapshot(IEnumerable<Exception> values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        var result = new List<Exception>();
        foreach (Exception value in values)
        { if (value == null) throw new ArgumentException("An inner exception is null."); result.Add(value); }
        return result;
    }
    public ReadOnlyCollection<Exception> InnerExceptions => _inner;
    public override string Message
    {
        get
        {
            string result = base.Message;
            for (int i = 0; i < _inner.Count; i++) result = string.Concat(result, " (", _inner[i].Message, ")");
            return result;
        }
    }
    public override Exception GetBaseException()
    {
        Exception result = this;
        while (result is AggregateException aggregate && aggregate.InnerExceptions.Count == 1) result = aggregate.InnerExceptions[0];
        return result;
    }
    public AggregateException Flatten()
    {
        var pending = new Queue<AggregateException>(); pending.Enqueue(this);
        var flat = new List<Exception>();
        while (pending.TryDequeue(out AggregateException current))
            for (int i = 0; i < current._inner.Count; i++)
                if (current._inner[i] is AggregateException nested) pending.Enqueue(nested); else flat.Add(current._inner[i]);
        return new AggregateException(base.Message, flat);
    }
    public void Handle(Func<Exception, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        var remaining = new List<Exception>();
        for (int i = 0; i < _inner.Count; i++) if (!predicate(_inner[i])) remaining.Add(_inner[i]);
        if (remaining.Count != 0) throw new AggregateException(base.Message, remaining);
    }
}
