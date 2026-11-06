using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using Neo4j.Driver;

namespace BoltSharp.IntegrationTests.Infrastructure;

internal sealed class ProposalInfo
{
    public required int Slot { get; init; }

    public required string RawHex { get; init; }

    public required string Kind { get; init; }

    public string? Version { get; init; }

    public string? Range { get; init; }
}

/// <summary>Shared scenario building blocks: standard scripts, proposal decoding and evidence documents.</summary>
internal static class ScenarioSupport
{
    public const string ServerName = "Neo4j/5.26.0";
    public const string ConnectionId = "bolt-b0b-1";

    /// <summary>The minimal HELLO SUCCESS metadata the real driver requires (server + connection_id).</summary>
    public static ReplyChain ExpectHello(this BoltConnectionScript script) =>
        script.On("HELLO").Success(
            ("server", ProtocolFixtures.Str(ServerName)),
            ("connection_id", ProtocolFixtures.Str(ConnectionId)),
            ("hints", ProtocolFixtures.Map()));

    public static BoltConnectionScript PositiveScript()
    {
        var script = new BoltConnectionScript();
        script.ExpectHello();
        script.On("LOGON").SuccessEmpty();
        script.On("RESET").SuccessEmpty();
        script.On("GOODBYE").Close();
        return script;
    }

    public static IReadOnlyList<ProposalInfo> DecodeProposals(byte[] handshake)
    {
        var result = new List<ProposalInfo>();
        if (handshake.Length < 20)
        {
            return result;
        }

        for (var slot = 0; slot < 4; slot++)
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(handshake.AsSpan(4 + (slot * 4), 4));
            var raw = $"{value:X8}";
            if (value == 0)
            {
                result.Add(new ProposalInfo { Slot = slot, RawHex = raw, Kind = "placeholder" });
            }
            else if (value == 0x0000_01FF)
            {
                result.Add(new ProposalInfo { Slot = slot, RawHex = raw, Kind = "manifest-v1" });
            }
            else
            {
                var major = (int)(value & 0xFF);
                var minor = (int)((value >> 8) & 0xFF);
                var range = (int)((value >> 16) & 0xFF);
                if (range > 0)
                {
                    result.Add(new ProposalInfo
                    {
                        Slot = slot,
                        RawHex = raw,
                        Kind = "range",
                        Version = $"{major}.{minor}",
                        Range = $"{major}.{minor - range}..{major}.{minor}",
                    });
                }
                else
                {
                    result.Add(new ProposalInfo
                    {
                        Slot = slot,
                        RawHex = raw,
                        Kind = "concrete",
                        Version = $"{major}.{minor}",
                    });
                }
            }
        }

        return result;
    }

    public static object CaptureDocument(string scenario, WireCapture capture) => new
    {
        scenario,
        negotiated_version = capture.NegotiatedVersion,
        client_handshake_hex = WireCapture.HexSpaced(capture.ClientHandshake),
        server_handshake_hex = WireCapture.HexSpaced(capture.ServerHandshake),
        proposals = DecodeProposals(capture.ClientHandshake).Select(p => new
        {
            slot = p.Slot,
            raw = p.RawHex,
            kind = p.Kind,
            version = p.Version,
            range = p.Range,
        }),
        requests = capture.Requests.Select(r => new
        {
            sequence = r.Sequence,
            name = r.Name,
            unscripted = r.Unscripted,
            sensitive = r.Sensitive,
            description = r.SanitizedDescription,
        }),
        frames = capture.Frames.Select(f => new
        {
            sequence = f.Sequence,
            direction = f.Direction.ToString(),
            name = f.Name,
            sensitive = f.Sensitive,
            summary = f.Summary,
            hex = f.SanitizedHex,
        }),
        notes = capture.Notes,
    };

    public static object EnvironmentDocument() => new
    {
        os = RuntimeInformation.OSDescription,
        framework = RuntimeInformation.FrameworkDescription,
        driver = DriverVersion(),
        boltSharp_baseline = "a4429ef969a3f150c4ab1b4908a193ffaddcce2a",
    };

    public static string DriverVersion()
    {
        var assembly = typeof(GraphDatabase).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
