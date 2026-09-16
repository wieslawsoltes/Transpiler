using System;
using System.Collections.Generic;
using System.Linq;
public static class Program
{
 static int Disposed;
 static IEnumerable<int> Source(){try{yield return 1;yield return 2;yield return 2;yield return 3;}finally{Disposed++;}}
 static void Check(bool b){if(!b)throw new Exception("linq invariant");}
 public static void Main()
 {
  var data=Source().Distinct().Append(4).Prepend(0);Check(Disposed==0);
  Console.WriteLine(data.Sum());Check(Disposed==1);
  Console.WriteLine(Source().Union(Enumerable.Range(3,3)).Sum());
  Console.WriteLine(Source().Intersect(Enumerable.Range(2,4)).Sum());
  Console.WriteLine(Source().Except(Enumerable.Range(2,4)).Sum());
  var dictionary=Source().Distinct().ToDictionary(x=>x,x=>(long)x*9007199254740993L);Console.WriteLine(dictionary[3]);
  Check(Source().ToHashSet().Count==3);Check(Source().SequenceEqual(Source()));Check(!Source().SequenceEqual(Enumerable.Range(1,3)));
  Check(Source().Contains(2)&&!Source().Contains(0));
  Console.WriteLine(Enumerable.Empty<int>().DefaultIfEmpty(9).Single());Check(Enumerable.Empty<int>().SingleOrDefault()==0);
  Console.WriteLine(Source().Last());Console.WriteLine(Enumerable.Empty<int>().LastOrDefault());
  foreach(var x in Source().Reverse())Console.WriteLine(x);
  Console.WriteLine(Enumerable.Range(1,3).SelectMany(x=>Enumerable.Range(x,2)).Sum());
  Console.WriteLine(Enumerable.Range(1,3).SelectMany(x=>Enumerable.Range(x,2),(x,y)=>x*y).Sum());
  try{Source().ToDictionary(x=>x);}catch(ArgumentException){Console.WriteLine("duplicate keys");}
  try{Source().Single();}catch(InvalidOperationException){Console.WriteLine("single checked");}
  try{Enumerable.Distinct<int>(null);}catch(ArgumentNullException){Console.WriteLine("eager validation");}
  int before=Disposed;foreach(var x in Source().Distinct()){break;}Check(Disposed==before+1);
  Console.WriteLine("linq collections verified");
 }
}
