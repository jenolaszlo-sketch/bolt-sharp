using System.Text.Json;

namespace BoltSharp.IntegrationTests.Infrastructure;

/// <summary>
/// Writes reproducible, sanitized evidence files when <c>BOLTSHARP_B0B_EVIDENCE_DIR</c> is set.
/// When the variable is unset the writers are no-ops, so ordinary test runs leave the working tree
/// unchanged. Authentication material is never passed to these writers.
/// </summary>
internal static class EvidenceSink
{
    internal const string DirectoryVariable = "BOLTSHARP_B0B_EVIDENCE_DIR";

    internal static string? Directory =>
        Environment.GetEnvironmentVariable(DirectoryVariable);

    internal static bool Enabled => !string.IsNullOrWhiteSpace(Directory);

    internal static void WriteJson(string fileName, object document)
    {
        var directory = Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, fileName), json + Environment.NewLine);
    }

    internal static void WriteText(string fileName, string text)
    {
        var directory = Directory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), text + Environment.NewLine);
    }
}
