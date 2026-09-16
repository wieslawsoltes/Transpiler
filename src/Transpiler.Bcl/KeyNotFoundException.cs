using System;
namespace Transpiler.Bcl;

/// <summary>Portable exception implementation; its inheritance and constructor execute as ordinary managed IL.</summary>
public class KeyNotFoundException : SystemException
{
    public KeyNotFoundException() : base("The given key was not present in the dictionary.") { }
    public KeyNotFoundException(string? message) : base(message) { }
}
