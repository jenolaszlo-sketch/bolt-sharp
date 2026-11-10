using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>
/// Reviewer-owned deterministic test credentials. The values are deliberately different from the
/// producer's harness credentials so any accidental hard-coding of producer secrets would fail.
/// Synthetic loopback test values only; never real secrets, never written to evidence.
/// </summary>
internal static class Q1Credentials
{
    internal const string User = "q1-reviewer-user";

    internal const string Password = "q1-reviewer-secret-4e7a";

    /// <summary>The deterministic verifier injected into reviewer servers under test.</summary>
    internal static IBoltCredentialVerifier Verifier { get; } = new DeterministicVerifier();

    /// <summary>Accepts exactly one synthetic basic principal/credential pair; fails closed.</summary>
    private sealed class DeterministicVerifier : IBoltCredentialVerifier
    {
        public bool Verify(LogonRequest logon)
        {
            string? scheme = null;
            string? principal = null;
            string? credentials = null;
            foreach (var entry in logon.Auth.Entries)
            {
                if (entry.Value is not PackStreamString text)
                {
                    continue;
                }

                switch (entry.Key)
                {
                    case "scheme":
                        scheme = text.Value;
                        break;
                    case "principal":
                        principal = text.Value;
                        break;
                    case "credentials":
                        credentials = text.Value;
                        break;
                }
            }

            return string.Equals(scheme, "basic", StringComparison.Ordinal)
                && string.Equals(principal, User, StringComparison.Ordinal)
                && string.Equals(credentials, Password, StringComparison.Ordinal);
        }
    }
}
