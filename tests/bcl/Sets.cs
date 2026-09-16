using System;
using System.Collections;
using System.Collections.Generic;
public class Modulo : IEqualityComparer<int>
{public bool Equals(int a,int b)=>a%7==b%7;public int GetHashCode(int v)=>v%7;}
public static class Program
{
 static void Check(bool b){if(!b)throw new Exception("set invariant");}
 static IEnumerable<int> Input(){yield return 1;yield return 2;yield return 2;yield return 3;}
 public static void Main()
 {
  var s=new HashSet<int>(Input());Check(s.Count==3&&!s.Add(2));
  Check(s.SetEquals(Input()));Check(s.IsSupersetOf(Input()));Check(s.IsSubsetOf(Input()));Check(!s.IsProperSubsetOf(Input()));
  s.UnionWith(s);Check(s.Count==3);s.IntersectWith(s);Check(s.Count==3);
  var t=new HashSet<int>();t.Add(2);t.Add(4);s.SymmetricExceptWith(t);Check(s.Contains(1)&&!s.Contains(2)&&s.Contains(3)&&s.Contains(4));
  Check(s.Overlaps(t)&&s.IsProperSupersetOf(new int[0]));
  s.IntersectWith(Input());Check(s.Count==2);Check(s.IsProperSubsetOf(Input()));
  s.ExceptWith(Input());Check(s.Count==0);s.UnionWith(Input());s.SymmetricExceptWith(s);Check(s.Count==0);
  var n=new HashSet<string>(StringComparer.Ordinal);n.Add(null);n.Add("x");Check(n.Contains(null));
  Check(n.TryGetValue("x",out var actual)&&actual=="x");Check(n.Remove(null));
  var m=new HashSet<int>(new Modulo());for(int i=0;i<100;i++)m.Add(i);Check(m.Count==7&&m.TryGetValue(36,out var stored)&&stored==1);
  Check(m.RemoveWhere(x=>x%2==0)==4);Check(m.Count==3);
  var e=m.GetEnumerator();e.MoveNext();m.Remove(3);Check(e.MoveNext());m.Add(0);
  try{e.MoveNext();throw new Exception("version");}catch(InvalidOperationException){Console.WriteLine("set version");}
  e=m.GetEnumerator();e.MoveNext();m.Clear();Check(!e.MoveNext());
  ICollection<int> coll=m;coll.Add(3);Check(!coll.IsReadOnly);IReadOnlySet<int> ro=m;Check(ro.Contains(3));
  var copied=new int[5];m.CopyTo(copied,1,3);Check(copied[1]==3&&copied[2]==0);
  IEnumerator boxed=((IEnumerable)m).GetEnumerator();try{var value=boxed.Current;}catch(InvalidOperationException){Console.WriteLine("set state");}
  boxed.MoveNext();boxed.Reset();Check(boxed.MoveNext());
  try{m.CopyTo(new int[0],1,0);}catch(ArgumentException){Console.WriteLine("copy bounds");}
  try{m.UnionWith(null);}catch(ArgumentNullException){Console.WriteLine("null source");}
  m.ExceptWith(m);Check(m.Count==0);Console.WriteLine("sets verified");
 }
}
