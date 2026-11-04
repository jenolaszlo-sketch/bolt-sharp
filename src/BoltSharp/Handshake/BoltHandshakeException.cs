namespace BoltSharp.Handshake;

/// <summary>Base type for errors raised by the Bolt handshake layer.</summary>
public class BoltHandshakeException : Exception
{
    public BoltHandshakeException()
    {
    }

    public BoltHandshakeException(string message) : base(message)
    {
    }

    public BoltHandshakeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The handshake bytes are structurally invalid: the Bolt magic is wrong, a version proposal
/// uses the reserved byte, a minor-version range is malformed or spans a major version, a
/// VarInt is not a valid bounded unsigned 64-bit value, a version list exceeds the configured
/// count, or a manifest selection is not a concrete 4.0-style version.
/// </summary>
public sealed class BoltHandshakeFormatException : BoltHandshakeException
{
    public BoltHandshakeFormatException(string message) : base(message)
    {
    }

    public BoltHandshakeFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The stream ended before a complete handshake element (magic, version proposal, VarInt or
/// manifest selection) could be read. This is the deterministic representation of a client
/// that disconnects part-way through the handshake.
/// </summary>
public sealed class BoltHandshakeTruncatedException : BoltHandshakeException
{
    public BoltHandshakeTruncatedException(string message) : base(message)
    {
    }
}

/// <summary>
/// A correctly encoded handshake violates the negotiation rules: a manifest client selected a
/// version the server did not offer, or selected capability bits the server did not offer.
/// No negotiated state is published when this is raised.
/// </summary>
public sealed class BoltHandshakeProtocolException : BoltHandshakeException
{
    public BoltHandshakeProtocolException(string message) : base(message)
    {
    }
}
