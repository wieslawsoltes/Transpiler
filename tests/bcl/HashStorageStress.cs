using System;
using System.Collections.Generic;
public class Collision : IEqualityComparer<int>
{ public bool Equals(int a,int b)=>a==b; public int GetHashCode(int a)=>0; }
public static class Program
{
 static void Check(bool value){if(!value)throw new Exception("hash storage mismatch");}
 public static void Main()
 {
  var d=new Dictionary<int,long>(new Collision());var set=new HashSet<int>(new Collision());
  var present=new bool[80];var expected=new long[80];int count=0;uint random=83281;
  for(int step=0;step<1000;step++)
  {
   random=unchecked(random*1664525+1013904223);int key=(int)(random%80);int op=(int)((random>>24)%6);
   long value=9007199254740993L+step;
   if(op==0){Check(d.Remove(key,out var removed)==present[key]);Check(removed==(present[key]?expected[key]:0));Check(set.Remove(key)==present[key]);if(present[key])count--;present[key]=false;}
   else if(op==1){Check(d.TryAdd(key,value)==!present[key]);Check(set.Add(key)==!present[key]);if(!present[key]){expected[key]=value;present[key]=true;count++;}}
   else if(op==2){d[key]=value;set.Add(key);if(!present[key])count++;expected[key]=value;present[key]=true;}
   else if(op==3){Check(d.TryGetValue(key,out var found)==present[key]);Check(found==(present[key]?expected[key]:0));}
   else if(op==4){Check(d.ContainsKey(key)==present[key]&&set.Contains(key)==present[key]);}
   else {d.TrimExcess();set.TrimExcess();}
   Check(d.Count==count&&set.Count==count);
   if(step%50==0){int seen=0;foreach(var pair in d){Check(present[pair.Key]&&pair.Value==expected[pair.Key]);seen++;}Check(seen==count);}
  }
  d.EnsureCapacity(160);set.EnsureCapacity(160);int final=0;
  for(int i=0;i<80;i++){Check(d.ContainsKey(i)==present[i]&&set.Contains(i)==present[i]);if(present[i])final++;}
  Check(final==count);Console.WriteLine(count);Console.WriteLine("1000 collision operations verified");
 }
}
