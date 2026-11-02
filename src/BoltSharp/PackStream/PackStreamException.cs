namespace BoltSharp.PackStream;

/// <summary>Base type for errors raised while encoding or decoding PackStream values.</summary>
public class PackStreamException : Exception
{
    public PackStreamException()
    {
    }

    public PackStreamException(string message) : base(message)
    {
    }

    public PackStreamException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The input contained a marker or value that is not valid PackStream for the requested type.
/// </summary>
public sealed class PackStreamFormatException : PackStreamException
{
    public PackStreamFormatException(string message) : base(message)
    {
    }
}

/// <summary>The input ended before a complete value could be read.</summary>
public sealed class PackStreamTruncatedException : PackStreamException
{
    public PackStreamTruncatedException(string message) : base(message)
    {
    }
}
