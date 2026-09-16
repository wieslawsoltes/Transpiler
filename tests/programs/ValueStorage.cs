using System;
public struct Point { public int X; public int Y; public Point(int x,int y) { X=x;Y=y; } public void Move(int n) { X+=n; } }
public struct Shape { public Point P; public string Name; }
public class Holder { public Shape Value; }
public static class Program
{
    static Point Copy(Point p) { p.X += 10; return p; }
    public static void Main()
    {
        var a = new Point(2,3); var b = a; b.Move(4); Console.WriteLine(a.X); Console.WriteLine(b.X);
        var c = Copy(a); Console.WriteLine(c.X); Console.WriteLine(a.X);
        object boxed = a; a.X=50; var unboxed = (Point)boxed; Console.WriteLine(unboxed.X);
        unboxed.X=99; Console.WriteLine(((Point)boxed).X);
        var shape = new Shape(); shape.P=a; var duplicate=shape; duplicate.P.X=22; Console.WriteLine(shape.P.X);
        ref int alias=ref shape.P.X; shape = new Shape(); alias=123; Console.WriteLine(shape.P.X);
        var array = new Point[2]; array[0]=a; array[0].Move(5); Console.WriteLine(array[0].X); Console.WriteLine(array[1].X);
        var holder=new Holder(); holder.Value=shape; var v=holder.Value; v.P.X=9; Console.WriteLine(holder.Value.P.X);
    }
}
