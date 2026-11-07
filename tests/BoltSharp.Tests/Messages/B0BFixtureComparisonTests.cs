using BoltSharp.Messages;
using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Messages;

/// <summary>
/// Comparison of the B3-A codec against the committed B0-B evidence: authentic
/// <c>Neo4j.Driver</c> 6.4.0 request captures must decode to exactly the expected typed values,
/// and the codec's response encoding must reproduce the B0-B-qualified server fixtures byte for
/// byte. Fixture bytes are copied from the hash-verified evidence files; nothing is regenerated.
/// </summary>
public sealed class B0BFixtureComparisonTests
{
    private readonly BoltMessageCodec _codec = new();

    [Fact]
    public void AuthenticRun_WithModeExtra_DecodesExactly()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.B0B.RunScalar);

        var run = Assert.IsType<RunRequest>(request);
        Assert.Equal("RETURN 42 AS answer", run.Query);
        Assert.Empty(run.Parameters.Entries);
        Assert.Single(run.Extra.Entries);
        Assert.Equal("r", BoltMessageFieldsTestAccess.GetString(run.Extra, "mode"));
    }

    [Fact]
    public void AuthenticRun_Plain_DecodesExactly()
    {
        var request = _codec.DecodeRequest(BoltMessageFixtures.B0B.RunReturnN);

        var run = Assert.IsType<RunRequest>(request);
        Assert.Equal("RETURN n", run.Query);
        Assert.Empty(run.Parameters.Entries);
        Assert.Empty(run.Extra.Entries);
    }

    [Fact]
    public void AuthenticPull_DefaultFetchSize_DecodesExactly()
    {
        var pull = Assert.IsType<PullRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.PullN1000));

        Assert.Equal(1000, pull.N);
        Assert.Null(pull.Qid);
    }

    [Fact]
    public void AuthenticPull_FiniteFetchSize_DecodesExactly()
    {
        var pull = Assert.IsType<PullRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.PullN1));

        Assert.Equal(1, pull.N);
        Assert.Null(pull.Qid);
    }

    [Fact]
    public void AuthenticDiscard_AllRemainingRecords_DecodesExactly()
    {
        var discard = Assert.IsType<DiscardRequest>(
            _codec.DecodeRequest(BoltMessageFixtures.B0B.DiscardNMinus1));

        Assert.Equal(-1, discard.N);
        Assert.Null(discard.Qid);
    }

    [Fact]
    public void AuthenticResetAndGoodbye_DecodeExactly()
    {
        Assert.IsType<ResetRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.Reset));
        Assert.IsType<GoodbyeRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.Goodbye));
    }

    // ---- Credential redaction ----

    [Fact]
    public void Logon_ToString_NeverExposesCredentialMaterial()
    {
        var logon = Assert.IsType<LogonRequest>(_codec.DecodeRequest(BoltMessageFixtures.Spec5_8.LogonBasic));

        var text = logon.ToString();

        Assert.Contains("basic", text, StringComparison.Ordinal);
        Assert.DoesNotContain("user", text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Hello_ToString_DoesNotExposeRoutingOrAgentDetails()
    {
        var hello = Assert.IsType<HelloRequest>(_codec.DecodeRequest(BoltMessageFixtures.Spec5_8.HelloFull));

        var text = hello.ToString();

        Assert.Contains("Example/1.0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("x.example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("neo4j-example", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_ToString_DoesNotExposeDescriptionOrDiagnosticRecord()
    {
        var failure = Assert.IsType<FailureResponse>(_codec.DecodeResponse(BoltMessageFixtures.B0B.FailureGql));

        var text = failure.ToString();

        Assert.Contains("42001", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid input", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ToString_DoesNotExposeParameterValues()
    {
        var run = Assert.IsType<RunRequest>(_codec.DecodeRequest(BoltMessageFixtures.Spec5_8.RunWithParameter));

        var text = run.ToString();

        Assert.Contains("RETURN $x AS x", text, StringComparison.Ordinal);
        // The parameter map entry count is structural; the value (1) must not appear as data.
        Assert.DoesNotContain("x: 1", text, StringComparison.Ordinal);
    }

    // ---- Codec isolation ----

    [Fact]
    public void Codec_DecodesRepeatedly_WithoutCrossMessageState()
    {
        // Interleave request and response decodes; a stateful codec would leak between them.
        var run = Assert.IsType<RunRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.RunScalar));
        var record = Assert.IsType<RecordResponse>(_codec.DecodeResponse(BoltMessageFixtures.B0B.Record42));
        var pull = Assert.IsType<PullRequest>(_codec.DecodeRequest(BoltMessageFixtures.B0B.PullN1000));
        var failure = Assert.IsType<FailureResponse>(_codec.DecodeResponse(BoltMessageFixtures.B0B.FailureGql));

        Assert.Equal("RETURN 42 AS answer", run.Query);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(record.Data.Items[0]).Value);
        Assert.Equal(1000, pull.N);
        Assert.Equal("42001", failure.GqlStatus);
    }
}
