namespace BoltSharp.Framing;

/// <summary>Base type for errors raised by the Bolt chunked-message framing layer.</summary>
public class BoltFramingException : Exception
{
    public BoltFramingException()
    {
    }

    public BoltFramingException(string message) : base(message)
    {
    }

    public BoltFramingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The chunked byte stream cannot be decoded as Bolt framing: a declared message exceeds
/// the configured maximum size, or the boundaries are otherwise structurally invalid.
/// </summary>
public sealed class BoltFramingFormatException : BoltFramingException
{
    public BoltFramingFormatException(string message) : base(message)
    {
    }

    public BoltFramingFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The stream ended before a complete chunk header, chunk payload or message terminator
/// could be read.
/// </summary>
public sealed class BoltFramingTruncatedException : BoltFramingException
{
    public BoltFramingTruncatedException(string message) : base(message)
    {
    }
}
