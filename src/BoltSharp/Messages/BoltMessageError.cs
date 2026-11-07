namespace BoltSharp.Messages;

/// <summary>
/// Deterministic failure categories produced by the Bolt message codec. Every rejection raised by
/// <c>BoltMessageCodec</c> carries exactly one category so tests and the future B3-B state machine
/// can distinguish invalid, unknown and unsupported messages precisely.
/// </summary>
public enum BoltMessageErrorCategory
{
    /// <summary>Not an error; placeholder only.</summary>
    None = 0,

    /// <summary>The payload contained no bytes at all.</summary>
    EmptyPayload,

    /// <summary>The top-level value is not a PackStream structure (a Bolt message must be one).</summary>
    NotAStructure,

    /// <summary>The structure tag is not a message signature defined by Bolt 5.8 (including Bolt graph-structure tags used as top-level messages).</summary>
    UnknownSignature,

    /// <summary>The signature is defined by Bolt 5.8 but is not implemented by this codec (BEGIN, COMMIT, ROLLBACK, ROUTE).</summary>
    UnsupportedMessage,

    /// <summary>The structure's declared field count does not match the message's arity.</summary>
    StructureArity,

    /// <summary>A field required by the message schema is absent.</summary>
    MissingRequiredField,

    /// <summary>A present field has the wrong PackStream type for the message schema.</summary>
    InvalidFieldType,

    /// <summary>A present field has a value outside the schema-permitted domain.</summary>
    InvalidFieldValue,

    /// <summary>A field is present that Bolt 5.8 explicitly prohibits (for example the pre-5.7 FAILURE <c>code</c> field or the pre-5.1 HELLO authentication entries).</summary>
    ProhibitedField,

    /// <summary>The payload contains bytes beyond the single top-level structure.</summary>
    TrailingBytes,

    /// <summary>The payload ended before the complete structure could be read.</summary>
    Truncated,

    /// <summary>A nested value is not valid PackStream, or violates a configured PackStream limit.</summary>
    InvalidPackStreamValue,
}

/// <summary>
/// A deterministic Bolt message decoding or encoding failure. The exception message references
/// field names, expected types and categories only — never field values — so diagnostic output can
/// never leak authentication or query payload material. The original PackStream exception, when
/// present, is preserved as the inner exception.
/// </summary>
public sealed class BoltMessageException : Exception
{
    /// <summary>Creates a decoding failure with the given category, message signature and explanation.</summary>
    public BoltMessageException(
        BoltMessageErrorCategory category,
        string message,
        byte? signature = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
        Signature = signature;
    }

    /// <summary>The deterministic failure category.</summary>
    public BoltMessageErrorCategory Category { get; }

    /// <summary>The message signature being decoded or encoded, when known; otherwise <see langword="null"/>.</summary>
    public byte? Signature { get; }
}
