using System;
using System.Collections.Generic;

namespace Transpiler.Bcl.Tasks;

public partial class Task
{
    // Intrusive, removable internal observations are separate from queued user await-continuations.
    // A winner detaches loser observations, avoiding retained coordinators on indefinitely pending tasks.
    internal sealed class CompletionRegistration
    {
        internal Task? Owner;
        internal CompletionRegistration? Previous;
        internal CompletionRegistration? Next;
        internal Action? Callback;
        internal CompletionRegistration(Task owner, Action callback) { Owner = owner; Callback = callback; }
        internal void Detach()
        {
            if (Owner == null) return;
            if (Previous == null) Owner._observers = Next; else Previous.Next = Next;
            if (Next != null) Next.Previous = Previous;
            Owner = null; Previous = null; Next = null; Callback = null;
        }
    }
    private CompletionRegistration? _observers;
    internal CompletionRegistration? Observe(Action callback)
    {
        if (IsCompleted) { callback(); return null; }
        var registration = new CompletionRegistration(this, callback) { Next = _observers };
        if (_observers != null) _observers.Previous = registration;
        _observers = registration;
        return registration;
    }
    private void NotifyObservers()
    {
        while (_observers != null)
        {
            CompletionRegistration registration = _observers;
            Action callback = registration.Callback!;
            registration.Detach();
            callback();
        }
    }
    public void Wait()
    {
        WaitCompletion();
        if (IsFaulted) throw Exception!;
        if (IsCanceled) throw new AggregateException(_error!);
    }
    internal bool FailSequence(IEnumerable<Exception> exceptions)
    {
        if (exceptions == null) throw new ArgumentNullException(nameof(exceptions));
        var errors = new List<Exception>();
        foreach (Exception error in exceptions)
        { if (error == null) throw new ArgumentException("An exception is null."); errors.Add(error); }
        return FailMany(errors);
    }
    public AggregateException? Exception => IsFaulted ? new AggregateException(_errors!) : null;
    internal Exception[]? Errors => _errors;
    internal Exception? CompletionError => _error;
    private Exception[]? _errors;
    internal bool FailMany(List<Exception> errors)
    {
        if (errors.Count == 0) throw new ArgumentException("At least one exception is required.");
        return Finish(2, errors[0], errors.ToArray());
    }
    private static TTask[] Snapshot<TTask>(IEnumerable<TTask> tasks) where TTask : Task
    {
        if (tasks == null) throw new ArgumentNullException(nameof(tasks));
        var copy = new List<TTask>();
        foreach (TTask task in tasks)
        { if (task == null) throw new ArgumentException("The tasks collection contains a null task."); copy.Add(task); }
        return copy.ToArray();
    }
    public static Task WhenAll(params Task[] tasks) => WhenAll((IEnumerable<Task>)tasks);
    public static Task WhenAll(IEnumerable<Task> tasks)
    {
        Task[] copy = Snapshot(tasks);
        if (copy.Length == 0) return CompletedTask;
        var promise = new AllPromise<Task>(copy, new Task());
        promise.Start(); return promise.Completion;
    }
    public static Task<T[]> WhenAll<T>(params Task<T>[] tasks) => WhenAll((IEnumerable<Task<T>>)tasks);
    public static Task<T[]> WhenAll<T>(IEnumerable<Task<T>> tasks)
    {
        Task<T>[] copy = Snapshot(tasks);
        if (copy.Length == 0) return FromResult(new T[0]);
        var promise = new ResultAllPromise<T>(copy);
        promise.Start(); return promise.Result;
    }
    public static Task<Task> WhenAny(params Task[] tasks) => Any(Snapshot(tasks));
    public static Task<Task> WhenAny(IEnumerable<Task> tasks) => Any(Snapshot(tasks));
    public static Task<Task> WhenAny(Task first, Task second)
    { if (first == null || second == null) throw new ArgumentNullException(first == null ? nameof(first) : nameof(second)); return Any(new[] { first, second }); }
    public static Task<Task<T>> WhenAny<T>(params Task<T>[] tasks) => Any(Snapshot(tasks));
    public static Task<Task<T>> WhenAny<T>(IEnumerable<Task<T>> tasks) => Any(Snapshot(tasks));
    public static Task<Task<T>> WhenAny<T>(Task<T> first, Task<T> second)
    { if (first == null || second == null) throw new ArgumentNullException(first == null ? nameof(first) : nameof(second)); return Any(new[] { first, second }); }
    private static Task<TTask> Any<TTask>(TTask[] tasks) where TTask : Task
    {
        if (tasks.Length == 0) throw new ArgumentException("At least one task is required.");
        for (int i = 0; i < tasks.Length; i++) if (tasks[i].IsCompleted) return FromResult(tasks[i]);
        var promise = new AnyPromise<TTask>(tasks.Length);
        for (int i = 0; i < tasks.Length; i++)
        { TTask task = tasks[i]; promise.Registrations[i] = task.Observe(() => promise.Win(task)); }
        return promise.Result;
    }
    private class AllPromise<TTask> where TTask : Task
    {
        protected readonly TTask[] Tasks;
        internal readonly Task Completion;
        private int _remaining;
        internal AllPromise(TTask[] tasks, Task completion) { Tasks = tasks; Completion = completion; _remaining = tasks.Length; }
        internal void Start() { for (int i = 0; i < Tasks.Length; i++) Tasks[i].Observe(OneCompleted); }
        private void OneCompleted()
        {
            if (--_remaining != 0) return;
            var failures = new List<Exception>(); Exception? canceled = null;
            for (int i = 0; i < Tasks.Length; i++)
            {
                Task task = Tasks[i];
                if (task.IsFaulted) failures.AddRange(task.Errors!);
                else if (task.IsCanceled && canceled == null) canceled = task.CompletionError;
            }
            if (failures.Count != 0) Completion.FailMany(failures);
            else if (canceled != null) Completion.Finish(3, canceled);
            else Succeed();
        }
        protected virtual void Succeed() => Completion.Finish(1, null);
    }
    private sealed class ResultAllPromise<T> : AllPromise<Task<T>>
    {
        internal readonly Task<T[]> Result;
        internal ResultAllPromise(Task<T>[] tasks) : this(tasks, new Task<T[]>()) { }
        private ResultAllPromise(Task<T>[] tasks, Task<T[]> result) : base(tasks, result) { Result = result; }
        protected override void Succeed()
        {
            var values = new T[Tasks.Length];
            for (int i = 0; i < values.Length; i++) values[i] = Tasks[i].GetValue();
            Result.Complete(values);
        }
    }
    private sealed class AnyPromise<TTask> where TTask : Task
    {
        internal readonly Task<TTask> Result = new Task<TTask>();
        internal readonly CompletionRegistration?[] Registrations;
        internal AnyPromise(int count) { Registrations = new CompletionRegistration?[count]; }
        internal void Win(TTask task)
        {
            if (!Result.Complete(task)) return;
            for (int i = 0; i < Registrations.Length; i++) { Registrations[i]?.Detach(); Registrations[i] = null; }
        }
    }
}
