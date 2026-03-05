namespace ManagedCode.CodexSharp;

public abstract record UserInput;

public sealed record TextInput(string Text) : UserInput;

public sealed record LocalImageInput(string Path) : UserInput;
