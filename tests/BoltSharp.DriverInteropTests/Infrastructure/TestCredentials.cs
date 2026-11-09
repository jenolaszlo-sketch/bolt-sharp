using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace BoltSharp.DriverInteropTests.Infrastructure;

/// <summary>
/// Deterministic, test-only credentials and the explicit credential verifier handed to the real
/// B5-A transport. These are synthetic loopback test credentials, not real secrets; the password
/// value must never appear in evidence documents (enforced by <see cref="EvidenceWriter"/>).
/// </summary>
internal static class TestCredentials
{
    internal const string User = "b5b-interop-user";

    internal const string Password = "b5b-interop-secret-9f2c";

    /// <summary>The deterministic verifier injected into every server under test.</summary>
    internal static IBoltCredentialVerifier Verifier { get; } = new DeterministicVerifier();

    private sealed class DeterministicVerifier : IBoltCredentialVerifier
    {
        public bool Verify(LogonRequest logon)
        {
            // Structural validation already happened in the B3-A codec; this seam only decides
            // whether the basic principal/credentials pair matches the deterministic test values.
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
