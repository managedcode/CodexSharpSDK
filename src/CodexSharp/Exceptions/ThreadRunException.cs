namespace ManagedCode.CodexSharp.Exceptions;

public sealed class ThreadRunException : Exception
{
    public ThreadRunException(string message)
        : base(message)
    {
    }

    public ThreadRunException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
