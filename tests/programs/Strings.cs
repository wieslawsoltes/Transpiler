using System;
public static class Program
{
    static string Text() => "A\U0001F682B";
    public static void Main()
    {
        string text = Text();
        Console.WriteLine(text); Console.WriteLine(text.Length);
        for (int i = 0; i < text.Length; i++) Console.WriteLine((int)text[i]);
        Console.WriteLine(text.Substring(1, 2));
        string combined = string.Concat(text.Substring(1, 1), text.Substring(2, 1));
        Console.WriteLine(combined == "\U0001F682");
        Console.WriteLine(object.ReferenceEquals(text, text.Substring(0, text.Length)));
        Console.WriteLine(object.ReferenceEquals(text, string.Concat("", text)));
        string other = string.Concat("a", "b");
        Console.WriteLine(other == "ab"); Console.WriteLine(object.ReferenceEquals(other, "ab"));
        Console.WriteLine("hello".Equals("hello")); Console.WriteLine(string.Equals(null, null));
        Console.WriteLine(string.Concat((object)true, (object)123, (object)'X'));
        string empty = ""; Console.WriteLine(empty != null); Console.WriteLine((char)65);
        try { Console.WriteLine(text.Substring(99)); } catch (ArgumentOutOfRangeException) { Console.WriteLine("substring checked"); }
    }
}
