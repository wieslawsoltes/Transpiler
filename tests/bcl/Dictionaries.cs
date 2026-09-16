using System;
using System.Collections;
using System.Collections.Generic;
public struct Pair : IEquatable<Pair>
{
 public int X;public int Y;public Pair(int x,int y){X=x;Y=y;}
 public bool Equals(Pair p)=>X==p.X&&Y==p.Y;
 public override bool Equals(object p)=>p is Pair v&&Equals(v);
 public override int GetHashCode()=>1;
}
public class Collision : IEqualityComparer<string>
{public bool Equals(string a,string b)=>a==b;public int GetHashCode(string x)=>-1;}
public static class Program
{
 static void Check(bool b){if(!b)throw new Exception("dictionary invariant");}
 public static void Main()
 {
  var d=new Dictionary<string,int>(new Collision());var keys=d.Keys;var values=d.Values;
  for(int i=0;i<40;i++)d.Add(i.ToString(),i*i);
  Check(d.Count==40&&keys.Count==40&&values.Count==40&&d.Comparer is Collision);
  for(int i=0;i<40;i+=2)Check(d.Remove(i.ToString(),out var v)&&v==i*i);
  for(int i=0;i<40;i++){Check(d.ContainsKey(i.ToString())==(i%2==1));}
  for(int i=0;i<40;i+=2)Check(d.TryAdd(i.ToString(),i));
  Check(!d.TryAdd("1",999)&&d["1"]==1);d["1"]=123;
  Check(((ICollection<int>)values).Contains(123)&&keys.Contains("1"));
  var e=d.GetEnumerator();e.MoveNext();d.Remove("3");d["1"]=5;Check(e.MoveNext());
  d.Add("new",1);try{e.MoveNext();throw new Exception("version");}catch(InvalidOperationException){Console.WriteLine("insert version");}
  e=d.GetEnumerator();e.MoveNext();d.Clear();Check(!e.MoveNext()&&keys.Count==0);
  Check(d.EnsureCapacity(64)>=64);d["a"]=1;d["b"]=2;d.TrimExcess();Check(d.Count==2&&d["a"]==1);
  IEnumerator boxed=((IEnumerable)d).GetEnumerator();try{var v=boxed.Current;throw new Exception("state");}catch(InvalidOperationException){Console.WriteLine("state checked");}
  boxed.MoveNext();boxed.Reset();Check(boxed.MoveNext());
  var pairs=new KeyValuePair<string,int>[2];((ICollection<KeyValuePair<string,int>>)d).CopyTo(pairs,0);
  Console.WriteLine(pairs[0]); Console.WriteLine(pairs[1]);
  ICollection<KeyValuePair<string,int>> c=d;Check(c.Contains(new KeyValuePair<string,int>("a",1)));Check(!c.Remove(new KeyValuePair<string,int>("a",9)));Check(c.Remove(new KeyValuePair<string,int>("a",1)));
  IReadOnlyDictionary<string,int> ro=d;Check(ro.ContainsKey("b"));foreach(var key in ro.Keys)Console.WriteLine(key);foreach(var v in ro.Values)Console.WriteLine(v);
  try{((ICollection<string>)keys).Add("x");}catch(NotSupportedException){Console.WriteLine("readonly keys");}
  try{d.Add(null,0);}catch(ArgumentNullException){Console.WriteLine("null key");}
  try{d.Add("b",0);}catch(ArgumentException){Console.WriteLine("duplicate");}
  try{Console.WriteLine(d["missing"]);}catch(KeyNotFoundException){Console.WriteLine("missing");}
  var structs=new Dictionary<Pair,Pair>();var p=new Pair(1,2);structs.Add(p,p);p.X=4;
  var result=structs[new Pair(1,2)];result.X=7;Check(structs[new Pair(1,2)].X==1);
  var copy=new Dictionary<Pair,Pair>(structs);Check(copy.ContainsKey(new Pair(1,2)));
  IDictionary<Pair,Pair> id=copy;Check(id.Remove(new Pair(1,2))&&id.Count==0);
  var nullable=new Dictionary<int,int?>();nullable[1]=null;nullable[2]=42;Check(nullable.ContainsValue(null)&&nullable[2].Value==42);
  var floating=new Dictionary<double,int>();floating[double.NaN]=4;floating[-0.0]=5;Check(floating[double.NaN]==4&&floating[0.0]==5);
  Console.WriteLine("dictionary verified");
 }
}
