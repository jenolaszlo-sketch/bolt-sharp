using BoltSharp.Messages;

namespace BoltSharp.Sessions;

/// <summary>
/// The outcome of processing one request (or one byte payload) through the session: the ordered
/// responses the connection must send, the resulting state, and whether the connection must be
/// closed after sending them.
/// </summary>
public sealed class BoltSessionResult
{
    private static readonly IReadOnlyList<BoltResponse> NoResponses = Array.Empty<BoltResponse>();

    private BoltSessionResult(BoltConnectionState state, IReadOnlyList<BoltResponse> responses, bool shouldClose)
    {
        State = state;
        Responses = responses;
        ShouldClose = shouldClose;
    }

    /// <summary>The connection state after this exchange.</summary>
    public BoltConnectionState State { get; }

    /// <summary>The ordered responses to send, never <see langword="null"/> and never mutable.</summary>
    public IReadOnlyList<BoltResponse> Responses { get; }

    /// <summary>True when the connection must be closed after sending <see cref="Responses"/>.</summary>
    public bool ShouldClose { get; }

    /// <summary>True when the session is now terminal (<see cref="BoltConnectionState.Defunct"/>).</summary>
    public bool IsDefunct => State == BoltConnectionState.Defunct;

    /// <summary>Creates a result with a single response.</summary>
    public static BoltSessionResult Single(BoltConnectionState state, BoltResponse response, bool shouldClose = false) =>
        new(state, new[] { response }, shouldClose);

    /// <summary>Creates a result with an ordered response list.</summary>
    public static BoltSessionResult Of(
        BoltConnectionState state,
        IReadOnlyList<BoltResponse> responses,
        bool shouldClose = false) =>
        new(state, responses, shouldClose);

    /// <summary>Creates a result with no responses (for example a terminating GOODBYE).</summary>
    public static BoltSessionResult None(BoltConnectionState state, bool shouldClose) =>
        new(state, NoResponses, shouldClose);
}

/// <summary>
/// One request's processing outcome inside a batch, preserving protocol causality: the originating
/// request signature and that request's ordered responses.
/// </summary>
public sealed record BoltResponseEnvelope(
    byte RequestSignature,
    IReadOnlyList<BoltResponse> Responses,
    BoltConnectionState State);
