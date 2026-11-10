using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>
/// Reviewer evidence writer. When BOLTSHARP_B5BQ1_EVIDENCE_DIR is set, each scenario writes one
/// sanitized JSON document. The writer refuses to store any document containing the reviewer's
/// synthetic credential value in plain, hex or UTF-16-hex form, and the wire-tap transcript
/// already redacts LOGON payloads at the raw-chunk level before any byte reaches this writer.
/// </summary>
internal static class Q1Evidence
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string? Directory()
    {
        var path = Environment.GetEnvironmentVariable("BOLTSHARP_B5BQ1_EVIDENCE_DIR");
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    internal static void Write(string name, object document)
    {
        var directory = Directory();
        if (directory is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(document, Options);
        GuardRedacted(json);
        var file = System.IO.Path.Combine(directory, name + ".json");
        System.IO.File.WriteAllText(file, json, new UTF8Encoding(false));
    }

    /// <summary>Throws when the reviewer credential sentinel would be persisted.</summary>
    private static void GuardRedacted(string json)
    {
        var password = Q1Credentials.Password;
        if (json.Contains(password, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("evidence document contains the credential sentinel in plain form");
        }

        var hex = string.Concat(password.Select(c => ((byte)c).ToString("x2")));
        var hexUpper = string.Concat(password.Select(c => ((byte)c).ToString("X2")));
        if (json.Contains(hex, StringComparison.Ordinal) || json.Contains(hexUpper, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("evidence document contains the credential sentinel in hex form");
        }

        var utf16 = string.Concat(password.Select(c => ((byte)c).ToString("x2") + "00"));
        if (json.Contains(utf16, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("evidence document contains the credential sentinel in UTF-16 hex form");
        }
    }

    internal static object CounterDocument(Q1ServerCounters counters) => new
    {
        accepted = counters.Accepted,
        rejected_for_capacity = counters.RejectedForCapacity,
        active = counters.Active,
        completed = counters.Completed,
        failed = counters.Failed,
        tls_handshake_failures = counters.TlsHandshakeFailures,
        bolt_handshake_failures = counters.BoltHandshakeFailures,
        messages_processed = counters.MessagesProcessed,
    };

    internal static object ProvidersDocument(Q1ProviderLog? providers)
    {
        if (providers is null)
        {
            return Array.Empty<object>();
        }

        return providers.Providers.Select((p, index) => (object)new
        {
            connection_index = index,
            prepare_calls = p.PrepareCalls,
            operations_opened = p.OperationsOpened,
            dispose_calls = p.DisposeCalls,
            dispose_failures = p.DisposeFailures,
            read_row_calls = p.ReadRowCalls,
        });
    }

    internal static object MessagesDocument(IReadOnlyList<Q1WireMessage> messages) =>
        messages.Select(m => new { m.Name, m.Description, m.Hex, m.Redacted });
}
