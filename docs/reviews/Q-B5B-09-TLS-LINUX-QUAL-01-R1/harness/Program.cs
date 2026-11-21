using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BoltSharp.Transport;

namespace R1Harness;

internal sealed class Context
{
    internal required Journal Journal { get; init; }
    internal required X509Certificate2 Certificate { get; init; }
    internal required string CertificatePemPath { get; init; }
    internal required string CertificateKeyPath { get; init; }
    internal required string Python { get; init; }
    internal required string PeerScriptDir { get; init; }
    internal required string ResultsDir { get; init; }
    internal required string RunId { get; init; }
    internal required string Commit { get; init; }
    internal required string Tree { get; init; }
    internal required string Mode { get; init; }
    internal int UnobservedExceptions;

    internal string CertificateSha256
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = Parse(args);
        var results = Path.GetFullPath(options.GetValueOrDefault("results")
            ?? throw new ArgumentException("--results is required"));
        var runId = options.GetValueOrDefault("run-id") ?? throw new ArgumentException("--run-id is required");
        var python = options.GetValueOrDefault("python")
            ?? (OperatingSystem.IsWindows() ? "python" : "python3");
        var peerDir = options.GetValueOrDefault("peer-dir")
            ?? Path.Combine(AppContext.BaseDirectory, "peer");
        var mode = options.GetValueOrDefault("mode") ?? "full";

        if (!Directory.Exists(results))
            throw new DirectoryNotFoundException(results);
        if (File.Exists(Path.Combine(results, "campaign-journal.jsonl")))
            throw new InvalidOperationException("results directory is not fresh: campaign-journal.jsonl exists");

        using var certificate = Certificates.Create(out var certPem, out var keyPem);
        var certDir = Path.Combine(results, "cert");
        Directory.CreateDirectory(certDir);
        var certPath = Path.Combine(certDir, "server.crt");
        var keyPath = Path.Combine(certDir, "server.key");
        File.WriteAllText(certPath, certPem);
        File.WriteAllText(keyPath, keyPem);

        using var journal = new Journal(results, runId);
        var context = new Context
        {
            Journal = journal,
            Certificate = certificate,
            CertificatePemPath = certPath,
            CertificateKeyPath = keyPath,
            Python = python,
            PeerScriptDir = Path.GetFullPath(peerDir),
            ResultsDir = results,
            RunId = runId,
            Commit = options.GetValueOrDefault("commit") ?? "unspecified",
            Tree = options.GetValueOrDefault("tree") ?? "unspecified",
            Mode = mode
        };

        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, e) =>
        {
            Interlocked.Increment(ref context.UnobservedExceptions);
            e.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += onUnobserved;
        var started = Stopwatch.GetTimestamp();
        var campaignFailure = "";
        try
        {
            journal.Write(journal.Record("campaign-start") is { } start
                ? With(start, ("campaign", Campaign.Id), ("commit", context.Commit), ("tree", context.Tree),
                    ("mode", mode), ("platform", Environment.OSVersion.VersionString),
                    ("runtime", Environment.Version.ToString()), ("kernel", OperatingSystemProbe.Kernel()),
                    ("wsl", OperatingSystemProbe.IsWsl()),
                    ("native_linux", OperatingSystemProbe.IsNativeLinux()),
                    ("python", python), ("certificate_sha256", context.CertificateSha256))
                : throw new InvalidOperationException());

            var selected = mode switch
            {
                "production" => Campaign.Cells.Where(c => c.Id is "L1" or "L2" or "L7").ToArray(),
                "candidate" => Campaign.Cells.Where(c => c.Id is "L3" or "L4" or "L5" or "L6").ToArray(),
                _ => Campaign.Cells
            };

            foreach (var cell in selected)
            {
                if (cell.Kind.StartsWith("candidate-", StringComparison.Ordinal))
                    await Candidate.RunCellAsync(context, cell);
                else
                    await Production.RunCellAsync(context, cell);
            }

            if (mode == "full")
            {
                await Production.RunProductionPlaintextCapAsync(context);
                await Controls.RunNegativeControlsAsync(context);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            journal.Write(With(journal.Record("campaign-end"),
                ("defect", campaignFailure.Length > 0),
                ("failure", campaignFailure),
                ("unobserved_task_exceptions", context.UnobservedExceptions),
                ("elapsed_ms", Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3))));
        }
        catch (Exception ex)
        {
            campaignFailure = ex.GetType().Name + ":" + ex.Message;
            journal.Write(With(journal.Record("campaign-end"),
                ("defect", true),
                ("failure", campaignFailure),
                ("unobserved_task_exceptions", context.UnobservedExceptions),
                ("elapsed_ms", Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3))));
            Console.Error.WriteLine(campaignFailure);
            return 2;
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobserved;
        }

        var assembly = typeof(BoltTcpServer).Assembly;
        var assemblyHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant();
        journal.WriteMeta(new Dictionary<string, object?>
        {
            ["run_id"] = runId,
            ["campaign"] = Campaign.Id,
            ["commit"] = context.Commit,
            ["tree"] = context.Tree,
            ["mode"] = mode,
            ["platform"] = Environment.OSVersion.VersionString,
            ["runtime"] = Environment.Version.ToString(),
            ["kernel"] = OperatingSystemProbe.Kernel(),
            ["wsl"] = OperatingSystemProbe.IsWsl(),
            ["native_linux"] = OperatingSystemProbe.IsNativeLinux(),
            ["python"] = python,
            ["production_assembly"] = assembly.Location,
            ["production_assembly_sha256"] = assemblyHash,
            ["certificate_sha256"] = context.CertificateSha256,
            ["started_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["elapsed_ms"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3),
            ["unobserved_task_exceptions"] = context.UnobservedExceptions,
            ["status"] = "raw-complete"
        });

        Console.WriteLine($"campaign raw complete: {results}");
        return 0;
    }

    internal static Dictionary<string, object?> With(Dictionary<string, object?> record,
        params (string Key, object? Value)[] entries)
    {
        foreach (var (key, value) in entries)
            record[key] = value;
        return record;
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("unexpected argument " + args[i]);
            var key = args[i][2..];
            if (i + 1 >= args.Length)
                throw new ArgumentException("missing value for " + args[i]);
            values[key] = args[++i];
        }

        return values;
    }
}
