using System;
public enum Tiny : byte { Zero=0, One=1, Big=255 }
public enum Signed : int { Minus=-1, Zero=0, One=1 }
public struct Value { public int N; }
public struct Cell<T> { public T Item; }
public static class Program
{
    static void Check(Type type, object value)
    {
        var a=Array.CreateInstance(type,1);
        try {a.SetValue(value,0);Console.WriteLine(a.GetValue(0));}
        catch(InvalidCastException){Console.WriteLine("invalid cast");}
        catch(ArgumentException){Console.WriteLine("argument");}
    }
    public static void Main()
    {
        var types=new Type[]{typeof(bool),typeof(sbyte),typeof(byte),typeof(char),typeof(short),typeof(ushort),typeof(int),typeof(uint),typeof(long),typeof(ulong),typeof(float),typeof(double),typeof(Tiny),typeof(Signed),typeof(int?),typeof(Value),typeof(string),typeof(object)};
        var values=new object[]{true,(sbyte)-7,(byte)250,'A',(short)-300,(ushort)60000,-100000,3000000000u,-9007199254740993L,18446744073709551615UL,1.25f,1.5,Tiny.Big,Signed.Minus,(int?)123,(int?)null,new Value{N=3},"hello",null};
        for(int i=0;i<types.Length;i++)for(int j=0;j<values.Length;j++){Console.WriteLine(i);Console.WriteLine(j);Check(types[i],values[j]);}
        var generic=new Cell<int>[1,2];generic[0,1].Item=77;var boxed=generic.GetValue(0,1);Console.WriteLine(((Cell<int>)boxed).Item);
        var nested=new int[1,2][];nested[0,0]=new[]{3,4};Console.WriteLine(nested[0,0][1]);Console.WriteLine(nested.GetType()==typeof(int[,][]));
        var nullable=new int?[1,2];nullable.SetValue(5,0,0);nullable.SetValue(null,0,1);Console.WriteLine(nullable[0,0].Value);Console.WriteLine(nullable.GetValue(0,1)==null);
        try{nullable.SetValue((short)1,0,0);}catch(InvalidCastException){Console.WriteLine("nullable exact type");}
        var data=Array.CreateInstance(typeof(long),new[]{2,2},new[]{-1,-2});data.SetValue(42L,-1,-2);var en=data.GetEnumerator();while(en.MoveNext())Console.WriteLine(en.Current);
    }
}
