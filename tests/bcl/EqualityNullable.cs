using System;
using System.Collections.Generic;
public class Key : IEquatable<Key>, IComparable<Key>
{
    public int Value;
    public Key(int value) { Value=value; }
    public bool Equals(Key other) => other != null && Value == other.Value;
    public int CompareTo(Key other) => other == null ? 1 : Value.CompareTo(other.Value);
    public override bool Equals(object other) => other is Key key && Equals(key);
    public override int GetHashCode() => Value % 3;
    public override string ToString() => "key:" + Value.ToString();
    public bool BaseEquals(object other) => base.Equals(other);
    public string BaseText() => base.ToString();
}
public class Base { }
public class Sub : Base, IEquatable<Base>
{ public bool Equals(Base other) => true; }
public struct Point : IEquatable<Point>, IComparable<Point>
{
    public int X; public int Y;
    public Point(int x,int y) { X=x;Y=y; }
    public bool Equals(Point other) => X==other.X && Y==other.Y;
    public int CompareTo(Point other) => X.CompareTo(other.X);
    public override bool Equals(object other) => other is Point p && Equals(p);
    public override int GetHashCode() => X ^ Y;
    public override string ToString() => "point:" + X.ToString();
}
public struct Plain { public int X; public string Text; }
public struct Explicit : IEquatable<Explicit>
{
    public int Value;
    bool IEquatable<Explicit>.Equals(Explicit other) => Value==other.Value;
    public override int GetHashCode() => Value;
}
public static class Program
{
    static T Echo<T>(T value) => value;
    public static void Main()
    {
        var a=new Key(7);var b=new Key(7);
        Console.WriteLine(EqualityComparer<Key>.Default.Equals(a,b));
        Console.WriteLine(object.Equals(a,b)); Console.WriteLine(((object)a).Equals(b));
        Console.WriteLine(a.BaseEquals(b)); Console.WriteLine(a.BaseEquals(a));
        Console.WriteLine(a.BaseText()); Console.WriteLine((object)a);
        Console.WriteLine(((object)a).GetHashCode()==EqualityComparer<Key>.Default.GetHashCode(b));
        Console.WriteLine(Comparer<Key>.Default.Compare(new Key(1),new Key(4))<0);
        Console.WriteLine(EqualityComparer<Base>.Default.Equals(new Sub(),new Base()));
        var p=new Point(2,3);Console.WriteLine(EqualityComparer<Point>.Default.Equals(p,new Point(2,3)));
        Console.WriteLine((object)p); Console.WriteLine(object.Equals(p,new Point(2,3)));
        Console.WriteLine(Comparer<Point>.Default.Compare(p,new Point(5,0))<0);
        Console.WriteLine(EqualityComparer<Plain>.Default.Equals(new Plain{X=3,Text="hi"},new Plain{X=3,Text="hi"}));
        Console.WriteLine(EqualityComparer<Explicit>.Default.Equals(new Explicit{Value=3},new Explicit{Value=3}));
        Console.WriteLine(EqualityComparer<double>.Default.Equals(double.NaN,double.NaN));
        Console.WriteLine(EqualityComparer<double>.Default.GetHashCode(-0.0)==EqualityComparer<double>.Default.GetHashCode(0.0));
        Console.WriteLine(Comparer<double>.Default.Compare(double.NaN,0)<0);
        var custom=EqualityComparer<Key>.Create((x,y)=>x.Value%2==y.Value%2,x=>x.Value%2);
        Console.WriteLine(custom.Equals(a,new Key(9))); Console.WriteLine(custom.GetHashCode(a));
        Console.WriteLine(Comparer<int>.Create((x,y)=>y.CompareTo(x)).Compare(1,3)>0);
        Console.WriteLine(StringComparer.Ordinal.Compare("\uD83D\uDE80","\uE000")<0);
        Console.WriteLine(StringComparer.Ordinal.Equals("A","a"));
        Console.WriteLine(StringComparer.Ordinal.GetHashCode("same")==StringComparer.Ordinal.GetHashCode("same"));
        int? n=Echo<int?>(null); int? m=Echo<int?>(42);
        Console.WriteLine(n.HasValue);Console.WriteLine(n.GetValueOrDefault());Console.WriteLine(n.GetValueOrDefault(9));
        Console.WriteLine(m.Value);Console.WriteLine(n==null);Console.WriteLine((object)n==null);
        Console.WriteLine((int)(object)m); Console.WriteLine(((int?)(object)42).Value);
        Console.WriteLine(((int?)(object)null).HasValue); Console.WriteLine(Nullable.Equals(m,(int?)42));
        Console.WriteLine(Nullable.Compare(n,m)<0); Console.WriteLine(EqualityComparer<int?>.Default.Equals(m,(int?)42));
        Console.WriteLine(EqualityComparer<int?>.Default.GetHashCode(n)); Console.WriteLine(m.ToString());
        Point? point=p;object boxed=point;Console.WriteLine(boxed is Point);Console.WriteLine(((Point?)boxed).Value.X);
        Console.WriteLine(EqualityComparer<Point?>.Default.Equals(point,(Point?)p));
        try{Console.WriteLine(n.Value);}catch(InvalidOperationException){Console.WriteLine("missing nullable");}
        try{Console.WriteLine(((int?)(object)1L).Value);}catch(InvalidCastException){Console.WriteLine("nullable type checked");}
    }
}
