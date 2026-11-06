namespace BoltSharp.IntegrationTests.Infrastructure;

/// <summary>
/// How the server writes a reply's framed bytes to the socket. Used to prove that a Bolt reader
/// must never assume a TCP write boundary is a message boundary.
/// </summary>
internal enum WriteSplitMode
{
    /// <summary>One write per framed message (default).</summary>
    Whole,

    /// <summary>One write per byte of the framed message.</summary>
    PerByte,

    /// <summary>Writes of at most <see cref="Reply.WriteChunkSize"/> framed bytes.</summary>
    FixedChunk,
}

/// <summary>One scripted reply: zero or more framed messages plus optional close/splitting.</summary>
internal sealed class Reply
{
    internal List<byte[]> Messages { get; } = new();

    /// <summary>Bytes written verbatim (not framed); used to emit deliberately incomplete frames.</summary>
    internal List<byte[]> RawWrites { get; } = new();

    internal bool CloseAfter { get; set; }

    internal WriteSplitMode SplitMode { get; set; } = WriteSplitMode.Whole;

    internal int WriteChunkSize { get; set; } = 7;

    /// <summary>When true, all of this reply's messages are concatenated into a single write.</summary>
    internal bool Coalesce { get; set; }
}

internal sealed class ReplyChain
{
    private readonly Reply _reply;

    internal ReplyChain(Reply reply) => _reply = reply;

    public ReplyChain Success(params (string Key, byte[] Value)[] metadata)
    {
        _reply.Messages.Add(ProtocolFixtures.Success(metadata));
        return this;
    }

    public ReplyChain SuccessEmpty()
    {
        _reply.Messages.Add(ProtocolFixtures.SuccessEmpty());
        return this;
    }

    public ReplyChain Record(params byte[][] values)
    {
        _reply.Messages.Add(ProtocolFixtures.Record(values));
        return this;
    }

    public ReplyChain Failure(params (string Key, byte[] Value)[] metadata)
    {
        _reply.Messages.Add(ProtocolFixtures.Failure(metadata));
        return this;
    }

    public ReplyChain Ignored(params (string Key, byte[] Value)[] metadata)
    {
        _reply.Messages.Add(ProtocolFixtures.Ignored(metadata));
        return this;
    }

    /// <summary>Adds a raw (unframed) message payload.</summary>
    public ReplyChain Send(byte[] message)
    {
        _reply.Messages.Add(message);
        return this;
    }

    /// <summary>Writes raw bytes verbatim (no chunk framing) before any close.</summary>
    public ReplyChain Raw(params byte[] bytes)
    {
        _reply.RawWrites.Add(bytes);
        return this;
    }

    public ReplyChain Close()
    {
        _reply.CloseAfter = true;
        return this;
    }

    public ReplyChain Split(WriteSplitMode mode, int chunkSize = 7)
    {
        _reply.SplitMode = mode;
        _reply.WriteChunkSize = chunkSize;
        return this;
    }

    public ReplyChain Coalesce()
    {
        _reply.Coalesce = true;
        return this;
    }
}

/// <summary>
/// A declarative set of expected requests and their replies. Replies for a given message name are
/// consumed in the order declared; once exhausted, the last reply for that name repeats (so a
/// repeated <c>PULL</c> is answered by the same terminal reply unless a distinct one is queued).
/// </summary>
internal sealed class BoltConnectionScript
{
    private readonly Dictionary<string, List<Reply>> _replies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _consumed = new(StringComparer.Ordinal);

    public ReplyChain On(string messageName)
    {
        if (!_replies.TryGetValue(messageName, out var list))
        {
            list = new List<Reply>();
            _replies[messageName] = list;
        }

        var reply = new Reply();
        list.Add(reply);
        return new ReplyChain(reply);
    }

    internal Reply? Next(string messageName)
    {
        if (!_replies.TryGetValue(messageName, out var list) || list.Count == 0)
        {
            return null;
        }

        var index = _consumed.TryGetValue(messageName, out var used) ? used : 0;
        if (index >= list.Count)
        {
            index = list.Count - 1;
        }
        else
        {
            _consumed[messageName] = index + 1;
        }

        return list[index];
    }

    internal bool IsScripted(string messageName) =>
        _replies.TryGetValue(messageName, out var list) && list.Count > 0;
}
