using System;
using System.Collections;
using System.Collections.Generic;
public static class Program
{
 static void Check(bool b){if(!b)throw new Exception("list invariant");}
 public static void Main()
 {
  var l=new List<int>();for(int i=0;i<100;i++)l.Add((i*17)%101);
  IList<int> il=l;IReadOnlyList<int> ro=l;Check(!il.IsReadOnly&&ro.Count==100);Check(il.Contains(0));
  l.Sort();for(int i=1;i<l.Count;i++)Check(l[i-1]<=l[i]);Check(l.BinarySearch(17)>=0&&l[l.BinarySearch(17)]==17);Check(l.BinarySearch(200)==~100);
  l.Sort((a,b)=>b.CompareTo(a));for(int i=1;i<l.Count;i++)Check(l[i-1]>=l[i]);
  l.Reverse();Check(l[0]==0);l.Reverse(10,5);l.Sort(10,5,Comparer<int>.Default);
  var arr=new int[l.Count+2];il.CopyTo(arr,1);Check(arr[1]==l[0]);
  Check(il.Remove(0)&&!il.Remove(0));l.InsertRange(0,l.GetRange(0,3));Check(l.IndexOf(l[0])==0&&l.LastIndexOf(l[0])>=3);
  var slice=l.Slice(0,3);slice.InsertRange(1,slice);Check(slice.Count==6);slice.RemoveRange(0,3);Check(slice.Count==3);
  Check(l.EnsureCapacity(200)>=200);l.TrimExcess();Check(l.Capacity>=l.Count);
  var found=l.FindAll(x=>x%2==0);Check(found.TrueForAll(x=>x%2==0));Check(l.Exists(x=>x==17));Check(l.Find(x=>x==17)==17);
  Check(l.FindIndex(1,l.Count-1,x=>x==17)>=1);Check(l.FindLastIndex(x=>x==17)>=0);Check(l.FindLast(x=>x==17)==17);
  var converted=slice.ConvertAll(x=>(long)x+9007199254740993L);Check(converted[0]==slice[0]+9007199254740993L);
  var e=l.GetEnumerator();e.MoveNext();il[0]=il[0];try{e.MoveNext();}catch(InvalidOperationException){Console.WriteLine("indexer version");}
  e=l.GetEnumerator();e.MoveNext();l.Sort();try{e.MoveNext();}catch(InvalidOperationException){Console.WriteLine("sort version");}
  try{l.Sort((a,b)=>throw new Exception("comparer error"));}catch(InvalidOperationException){Console.WriteLine("comparison failure");}
  try{l.FindIndex(-1,x=>true);}catch(ArgumentOutOfRangeException){Console.WriteLine("find bounds");}
  var text=new List<string>();text.Add("z");text.Add("a");text.Add("b");text.Sort(StringComparer.Ordinal);Check(text[0]=="a"&&text[2]=="z");
  Check(Comparer<object>.Default.Compare((object)5,(object)8)<0);
  Console.WriteLine("list operations verified");
 }
}
