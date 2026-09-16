using System;
public class Resource { ~Resource() { Console.WriteLine("requires managed finalization"); } }
public static class Program { public static void Main() { Console.WriteLine((object)new Resource()); } }
