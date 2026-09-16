using System;
using System.Collections;
public struct Point { public int X; public int Y; }
public class Box { public int Value; }
public static class Program
{
    static int I(int x)=>x;
    static void WriteType(Array a) { Console.WriteLine(a.GetType()); Console.WriteLine(a.GetType().IsArray); Console.WriteLine(a.GetType().GetArrayRank()); }
    public static void Main()
    {
        var a=new int[2,3];
        for(int i=0;i<2;i++)for(int j=0;j<3;j++)a[i,j]=i*10+j;
        foreach(int value in a) Console.WriteLine(value);
        ref int slot=ref a[1,2];slot+=100;Console.WriteLine(a[1,2]);
        Console.WriteLine(a.Rank);Console.WriteLine(a.Length);Console.WriteLine(a.LongLength);
        for(int i=0;i<a.Rank;i++){Console.WriteLine(a.GetLength(i));Console.WriteLine(a.GetLongLength(i));Console.WriteLine(a.GetLowerBound(i));Console.WriteLine(a.GetUpperBound(i));}
        WriteType(a);Console.WriteLine(a.GetType()==typeof(int[,]));Console.WriteLine(a.GetType().GetElementType()==typeof(int));
        var clone=(int[,])a.Clone();clone[1,2]=999;Console.WriteLine(a[1,2]);Console.WriteLine(clone[1,2]);
        Array.Clear(a,1,3);foreach(int value in a)Console.WriteLine(value);Array.Clear(a);Console.WriteLine(a[1,2]);
        Array lower=Array.CreateInstance(typeof(int),new[]{2,3},new[]{-2,4});
        lower.SetValue(11,-2,4);lower.SetValue((short)12,-1,6);lower.SetValue(13,new long[]{-1,5});
        Console.WriteLine(lower.GetValue(-2L,4L));Console.WriteLine(lower.GetValue(new[]{-1,6}));
        var typed=(int[,])lower;ref int address=ref typed[-1,5];address++;
        foreach(object item in lower)Console.WriteLine(item);
        for(int i=0;i<2;i++){Console.WriteLine(lower.GetLowerBound(i));Console.WriteLine(lower.GetUpperBound(i));}
        var copied=(int[,])lower.Clone();Console.WriteLine(copied[-1,5]);Array.Clear(lower,-1,3);Console.WriteLine(typed[-1,5]);
        var points=new Point[2,2];points[0,1].X=7;points[0,1].Y=8;var pointCopy=(Point[,])points.Clone();pointCopy[0,1].X=100;Console.WriteLine(points[0,1].X);
        points.SetValue(new Point{X=4,Y=6},1,1);var extracted=(Point)points.GetValue(1,1);extracted.Y=99;Console.WriteLine(points[1,1].Y);
        points.SetValue(null,1,1);Console.WriteLine(points[1,1].X);
        var boxes=new Box[1,1];boxes[0,0]=new Box{Value=5};var shared=(Box[,])boxes.Clone();shared[0,0].Value=9;Console.WriteLine(boxes[0,0].Value);
        object[,] covariant=new string[1,2];covariant[0,0]="text";Console.WriteLine(covariant[0,0]);
        try{covariant[0,1]=new object();}catch(ArrayTypeMismatchException){Console.WriteLine("covariant store checked");}
        try{ref object invalid=ref covariant[0,0];invalid=new object();}catch(ArrayTypeMismatchException){Console.WriteLine("covariant address checked");}
        var cube=new long[2,2,2];cube[1,1,1]=9007199254740993L;Console.WriteLine(cube.GetValue(1L,1L,1L));
        var rank7=Array.CreateInstance(typeof(byte),new[]{1,1,1,1,1,1,1});rank7.SetValue((byte)255,new[]{0,0,0,0,0,0,0});Console.WriteLine(rank7.GetValue(new[]{0,0,0,0,0,0,0}));
        var vector=Array.CreateInstance(typeof(int),new[]{2},new[]{0});Console.WriteLine(vector is int[]);Console.WriteLine(vector.GetType()==typeof(int[]));
        var rankOne=Array.CreateInstance(typeof(int),new[]{2},new[]{-5});Console.WriteLine(rankOne is int[]);WriteType(rankOne);rankOne.SetValue(9,-5);Console.WriteLine(rankOne.GetValue(-5));
        var empty=Array.CreateInstance(typeof(int),new[]{0,2},new[]{-5,4});Console.WriteLine(empty.Length);Console.WriteLine(empty.GetUpperBound(0));
        try{Console.WriteLine(a[2,0]);}catch(IndexOutOfRangeException){Console.WriteLine("typed bounds");}
        try{var bad=new int[I(-1),2];}catch(OverflowException){Console.WriteLine("negative length");}
        try{a.GetLength(2);}catch(IndexOutOfRangeException){Console.WriteLine("dimension checked");}
        try{a.GetValue(0);}catch(ArgumentException){Console.WriteLine("rank checked");}
        try{a.GetValue((int[])null);}catch(ArgumentNullException){Console.WriteLine("null indices");}
        try{a.GetValue(long.MaxValue,0L);}catch(ArgumentOutOfRangeException){Console.WriteLine("wide index checked");}
        try{a.SetValue("bad",0,0);}catch(InvalidCastException){Console.WriteLine("boxed store checked");}
        try{Array.CreateInstance(typeof(int),new[]{-1});}catch(ArgumentOutOfRangeException){Console.WriteLine("creation length checked");}
        try{Array.CreateInstance(null,2);}catch(ArgumentNullException){Console.WriteLine("null element checked");}
        try{Array.Clear(null);}catch(ArgumentNullException){Console.WriteLine("null clear checked");}
        try{Array.Clear(a,-1,2);}catch(IndexOutOfRangeException){Console.WriteLine("clear bounds checked");}
        try{Array.CreateInstance(typeof(int),new[]{2},new[]{0,0});}catch(ArgumentException){Console.WriteLine("bounds rank checked");}
        Console.WriteLine(typeof(int).GetElementType()==null);Console.WriteLine(!typeof(int).IsArray);
    }
}
