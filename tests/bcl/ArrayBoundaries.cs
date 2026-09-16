using System;
public static class Program
{
 static void Try(string name,Action action){Console.Write(name);Console.Write(": ");try{action();Console.WriteLine("ok");}catch(Exception e){Console.WriteLine(e.GetType());}}
 public static void Main(){
 Try("rank0",()=>Array.CreateInstance(typeof(int),new int[0]));
 Try("rank33",()=>Array.CreateInstance(typeof(int),new int[33]));
 Try("max lower",()=>Console.WriteLine(Array.CreateInstance(typeof(int),new[]{1},new[]{int.MaxValue}).GetUpperBound(0)));
 Try("min lower empty",()=>Console.WriteLine(Array.CreateInstance(typeof(int),new[]{0},new[]{int.MinValue}).GetUpperBound(0)));
 Try("max lower empty",()=>Console.WriteLine(Array.CreateInstance(typeof(int),new[]{0},new[]{int.MaxValue}).GetUpperBound(0)));
 Try("clear negative",()=>Array.Clear(new int[1],0,-1));
 Try("clear end empty",()=>Array.Clear(new int[1],1,0));
 Try("create void",()=>Array.CreateInstance(typeof(void),1));
 Try("type rank",()=>typeof(int).GetArrayRank());
 Try("params rank priority",()=>new int[1,1].GetValue(new long[]{long.MaxValue}));
 Try("typed rank priority",()=>new int[1,1].GetValue(long.MaxValue));
 }
}
