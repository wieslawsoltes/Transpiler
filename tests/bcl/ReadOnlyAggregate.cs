using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
public static class Program
{
    static void Check(bool condition) { if (!condition) throw new Exception("Contract mismatch"); }
    public static void Main()
    {
        var list = new List<int> { 1, 2, 3 }; var view = list.AsReadOnly(); list[1] = 42; list.Add(5);
        Console.WriteLine(view.Count); Console.WriteLine(view[1]); Console.WriteLine(view.Contains(42)); Console.WriteLine(view.IndexOf(5));
        foreach(int value in view) Console.WriteLine(value);
        var array = new int[6]; view.CopyTo(array,1); Console.WriteLine(array[2]);
        IList<int> mutable = view;
        try { mutable.Add(3); } catch(NotSupportedException) { Console.WriteLine("read only"); }
        try { mutable[0]=9; } catch(NotSupportedException) { Console.WriteLine("setter rejected"); }
        int[] backing={7,8,9}; var arrayView=new ReadOnlyCollection<int>(backing);backing[0]=11;
        Console.WriteLine(arrayView[0]); foreach(int value in arrayView) Console.WriteLine(value);
        Check(arrayView.Contains(8));
        try { int ignored = arrayView[-1]; } catch (ArgumentOutOfRangeException) { Console.WriteLine("view bounds checked"); }
        var a = new ArgumentException("alpha"); var b = new InvalidOperationException("beta");
        Exception[] errors = { a, b }; var aggregate = new AggregateException("group", errors); errors[0] = new Exception("replaced");
        Check(object.ReferenceEquals(aggregate.InnerException,a)); Check(object.ReferenceEquals(aggregate.InnerExceptions[0],a));
        Exception asBase = aggregate; Console.WriteLine(asBase.Message);
        var nested = new AggregateException("outer", new Exception[] { aggregate, new AggregateException(a) });
        var flat = nested.Flatten(); Check(flat.InnerExceptions.Count==3);
        foreach (Exception error in flat.InnerExceptions) Console.WriteLine(error.Message);
        var single = new AggregateException(new AggregateException(a)); Check(object.ReferenceEquals(single.GetBaseException(),a));
        try { aggregate.Handle(error=>error is ArgumentException); }
        catch(AggregateException unhandled) { Check(unhandled.InnerExceptions.Count==1 && object.ReferenceEquals(unhandled.InnerExceptions[0],b)); Console.WriteLine("remaining preserved"); }
        aggregate.Handle(error=>true);
        var empty=new AggregateException();Check(empty.InnerExceptions.Count==0);Check(object.ReferenceEquals(empty.GetBaseException(),empty));
        try { new AggregateException(new Exception[] { null }); } catch(ArgumentException) { Console.WriteLine("null inner rejected"); }
        try { new AggregateException((IEnumerable<Exception>)null); } catch(ArgumentNullException) { Console.WriteLine("null collection rejected"); }
        try { ((ICollection<Exception>)aggregate.InnerExceptions).Clear(); } catch(NotSupportedException) {Console.WriteLine("errors immutable");}
        Console.WriteLine("aggregate and views verified");
    }
}
