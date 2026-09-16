using System;
public class Label { public override string ToString() => "must not silently lose this override"; }
public static class Program { public static void Main() { Console.WriteLine((object)new Label()); } }
