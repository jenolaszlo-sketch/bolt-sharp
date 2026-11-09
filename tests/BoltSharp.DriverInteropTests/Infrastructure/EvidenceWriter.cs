using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using BoltSharp.Messages;
using BoltSharp.Transport;

namespace BoltSharp.DriverInteropTests.Infrastructure;

/// <summary>
/// Env-gated evidence writer. When <c>BOLTSHARP_B5B_EVIDENCE_DIR</c> is set, scenario documents
/// are serialized to that directory (one JSON file per scenario); otherwise the writer is a
/// no-op and tests still assert everything. Every document is scanned for the deterministic
/// credential sentinel before it is written: credential material can never reach the evidence.
/// </summary>
internal static class EvidenceWriter
{
    internal const string EvidenceDirectoryVariable = "BOLTSHARP_B5B_EVIDENCE_DIR";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private static readonly object Gate = new();
    private static bool _environmentWritten;

    internal static bool Enabled =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EvidenceDirectoryVariable));

    internal static string Serialize(object document) =>
        JsonSerializer.Serialize(document, Options);

    internal static void Write(string fileName, object document)
    {
        var json = Serialize(document);
        GuardAgainstCredentialMaterial(fileName, json);
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            var directory = Environment.GetEnvironmentVariable(EvidenceDirectoryVariable)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName + ".json"), json);
        }
    }

    /// <summary>Writes the environment document once per test process.</summary>
    internal static void WriteEnvironment()
    {
        lock (Gate)
        {
            if (_environmentWritten)
            {
                return;
            }

            _environmentWritten = true;
        }

        Write("00-environment", new
        {
            task = "B5-B-IMPL-01",
            os = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            driver_package = DriverFactory.PackageIdentity,
            driver_assembly_version = DriverFactory.PackageVersion,
            driver_informational_version = DriverFactory.InformationalVersion,
            server_assembly = ServerAssemblyVersion(),
            git_head = GitRevParse("HEAD"),
            git_tree = GitRevParse("HEAD^{tree}"),
            generated_utc = DateTimeOffset.UtcNow.ToString("O"),
        });
    }

    /// <summary>Server-side events rendered for evidence without payloads (details are metadata only).</summary>
    internal static object EventDocument(IEnumerable<BoltTransportDiagnosticEvent> events) =>
        events.Select(e => new
        {
            at_utc = e.TimestampUtc.ToString("O"),
            kind = e.Kind.ToString(),
            connection_id = e.ConnectionId,
            detail = e.Detail,
        });

    internal static object CounterDocument(ServerCounters counters) => new
    {
        connections_accepted = counters.Accepted,
        connections_rejected_for_capacity = counters.RejectedForCapacity,
        connections_active = counters.Active,
        connections_completed = counters.Completed,
        connections_failed = counters.Failed,
        tls_handshake_failures = counters.TlsHandshakeFailures,
        bolt_handshake_failures = counters.BoltHandshakeFailures,
        messages_processed = counters.MessagesProcessed,
    };

    /// <summary>Classification of a driver-observed exception for stable evidence.</summary>
    internal static object ExceptionDocument(Exception exception) => new
    {
        type = exception.GetType().FullName,
        message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim(),
        inner_types = InnerTypes(exception),
        neo4j_code = (exception as Neo4j.Driver.Neo4jException)?.Code,
        gql_status = (exception as Neo4j.Driver.Neo4jException)?.GqlStatus,
        gql_classification = (exception as Neo4j.Driver.Neo4jException)?.GqlClassification?.ToString(),
        gql_status_description = (exception as Neo4j.Driver.Neo4jException)?.GqlStatusDescription,
        retriable = (exception as Neo4j.Driver.Neo4jException)?.IsRetriable,
    };

    private static List<string> InnerTypes(Exception exception)
    {
        var result = new List<string>();
        var inner = exception.InnerException;
        while (inner is not null)
        {
            result.Add(inner.GetType().FullName ?? inner.GetType().Name);
            inner = inner.InnerException;
        }

        return result;
    }

    private static void GuardAgainstCredentialMaterial(string fileName, string json)
    {
        if (json.Contains(TestCredentials.Password, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Evidence document '{fileName}' contains the credential sentinel and was not written.");
        }

        // Also guard the hex-encoded form: captured byte chunks render credentials as hex.
        var hexSentinel = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(TestCredentials.Password));
        if (json.Contains(hexSentinel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Evidence document '{fileName}' contains the hex-encoded credential sentinel and was not written.");
        }
    }

    private static string ServerAssemblyVersion() =>
        typeof(BoltSharp.Transport.BoltTcpServer).Assembly.GetName().Version?.ToString() ?? "unknown";

    private static string GitRevParse(string revision)
    {
        try
        {
            var root = FindRepositoryRoot();
            if (root is null)
            {
                return "unavailable";
            }

            var startInfo = new ProcessStartInfo("git", $"rev-parse {revision}")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "unavailable";
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : "unavailable";
        }
        catch (Exception)
        {
            return "unavailable";
        }
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
