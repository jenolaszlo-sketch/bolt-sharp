using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Remediation;

/// <summary>
/// B1-R1 remediation regressions for finding F5 (MINOR) of
/// docs/reviews/B1-Q1-packstream-qualification.md: the reader-reuse hazard was not visible
/// in the public API documentation. The contract is now stated on PackStreamReader: the
/// reader tracks bytes consumed before a failure, is not realigned, and must be discarded
/// after any decoding exception. These tests support the documented semantics.
/// </summary>
public sealed class B1R1F5ReaderFailureContractTests
{
    [Fact]
    public void AfterFormatException_PositionTracksBytesConsumedBeforeFailure()
    {
        // B0 4E: a Node tag with an invalid zero-field arity. Both bytes are consumed
        // before the failure is raised.
        var reader = new PackStreamReader(new byte[] { 0xB0, 0x4E, 0x2A });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void AfterTruncation_PositionTracksBytesConsumedBeforeFailure()
    {
        var reader = new PackStreamReader(new byte[] { 0x93, 0x01 });

        Assert.Throws<PackStreamTruncatedException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void DocumentedRecovery_IsDiscardReaderAndCreateANewOne()
    {
        // After a decoding exception the reader must be discarded. The supported way to
        // continue with remaining bytes is a new reader over the unconsumed portion —
        // used here only because the failure boundary is known exactly. Reusing the failed
        // reader instead is unspecified (reviewer Probe09b documents the misalignment that
        // results).
        var bytes = new byte[] { 0xB0, 0x4E, 0x2A };
        var reader = new PackStreamReader(bytes);

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());

        var rest = bytes.AsSpan(reader.Position).ToArray();
        Assert.Equal(42L, new PackStreamReader(rest).ReadInteger());
    }

    [Fact]
    public void Position_TracksConsumedBytesBeforeEachFailure()
    {
        // Successful reads advance the position; a later failure still reports the exact
        // consumed-byte count at its failure point.
        var reader = new PackStreamReader(new byte[] { 0x01, 0xC8 });

        Assert.Equal(1L, reader.ReadInteger());
        Assert.Equal(1, reader.Position);

        Assert.Throws<PackStreamTruncatedException>(() => reader.ReadInteger());
        Assert.Equal(2, reader.Position);
    }
}
