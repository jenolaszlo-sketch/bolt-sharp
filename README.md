# BoltSharp

**An independent .NET implementation of the Bolt 5.8 / PackStream 1.0 wire protocol.**

BoltSharp develops reusable building blocks for communicating using Bolt, with a focus on enabling independently developed services and databases to expose Bolt-compatible server endpoints.

> **Independent project:** BoltSharp is not affiliated with, sponsored by, endorsed by, or maintained by Neo4j. Compatibility is an engineering objective, not a claim of official certification or complete Neo4j feature support.

## Status and scope

**Preview release (1.0.0-preview.1).** The Windows transport layer (Bolt 5.8 / PackStream 1.0) has been independently qualified. Full Bolt server functionality and interoperability with any particular driver or Neo4j Browser version should **not** be assumed from the project's name or roadmap.

The qualified scope includes:

- **Bolt 5.8 handshake** with Manifest v1 and legacy negotiation
- **PackStream 1.0** serialization and deserialization
- **TLS transport** with certificate-based server authentication (TLS 1.2/1.3)
- **Plaintext transport** for development and testing
- **Server-side session handling** with `HELLO`/`LOGON`/`RUN`/`PULL`/`DISCARD`/`RESET`/`GOODBYE`
- **Listener lifecycle** with sentinel-based termination and queue draining
- **TLS cancellation** propagating into the handshake (F-01 fix)
- **Listener fault accounting** via `ListenerFault` event (F-02 fix)

BoltSharp is **not** a graph database, query execution engine, or Cypher implementation. A service built on BoltSharp remains responsible for storage, query semantics, access control, and application behavior.

## Build and test

Use a compatible .NET SDK to build and test the solution:

```sh
dotnet build BoltSharp.slnx
dotnet test BoltSharp.slnx
```

Passing local tests does not, by itself, establish compatibility with third-party clients. Any interoperability claim should state the client version, negotiated protocol version, transport settings, exercised behavior, and test evidence.

## Installation

```sh
dotnet add package BoltSharp --version 1.0.0-preview.1
```

## Minimal usage

```csharp
using BoltSharp.Transport;
using BoltSharp.Sessions;
using BoltSharp.Messages;

using var certificate = CreateSelfSignedCertificate();
var tlsOptions = new BoltTlsServerOptions(certificate);

var server = BoltTcpServer.Create(new BoltTcpServerOptions
{
    BindAddress = IPAddress.Loopback,
    CredentialVerifier = MyCredentialVerifier.AcceptAll(),
    Tls = new BoltTlsServerOptions(certificate),
    TlsHandshakeTimeout = TimeSpan.FromSeconds(5),
    ShutdownTimeout = TimeSpan.FromSeconds(5),
});

await server.StartAsync();
Console.WriteLine($"Server listening on {server.BoundEndPoint}");

await server.StopAsync();
await server.DisposeAsync();
```

## License and third-party rights

**BoltSharp's original code is licensed under the [Apache License, Version 2.0](LICENSE).** Subject to its conditions, Apache 2.0 permits modification, redistribution, and commercial use of covered code.

The **Bolt protocol documentation published by Neo4j is a different work**. The [Neo4j Bolt documentation repository](https://github.com/neo4j/docs-bolt) identifies its documentation license as **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 (CC BY-NC-SA 4.0)**. That documentation license does not automatically become the license of independently written protocol implementation code. Conversely, BoltSharp's Apache license does not grant rights to copy Neo4j's documentation or code, or waive possible third-party patent, trademark, or other restrictions.

For the detailed licensing boundaries, original-implementation guidelines, trademark notices, and commercial-use qualifications, read **[Legal, Licensing, and Compatibility Notes](docs/legal-and-compatibility.md)**.

## Compatibility and trademarks

References to **Neo4j**, **Cypher**, or related products are for technical identification and interoperability discussion only. Neo4j and Cypher are trademarks or registered trademarks of Neo4j, Inc. or its subsidiaries.

A Bolt-compatible endpoint does not necessarily provide Neo4j database semantics or Cypher support. Features and driver versions are supported only to the extent established by reproducible tests.

## Qualification summary

| Capability | Status | Evidence |
|------------|--------|----------|
| Bolt 5.8 handshake | **Qualified** | B5BQ1Probe `tls-auth-cancel`, `ipv4/ipv6-loopback-negotiation` |
| PackStream 1.0 | **Qualified** | Transport suite, B5BQ1Probe flood scenarios |
| TLS transport | **Qualified** | `tls-auth-cancel`, `tls-abandoned-task` (0 unobserved) |
| Plaintext transport | **Qualified** | `ipv4/ipv6-loopback-negotiation` |
| Listener lifecycle | **Qualified** | Frozen test 30×, Lifecycle 5×, B5BQ1Probe all scenarios |
| TLS cancellation (F-01) | **Qualified** | 300/300 rounds → 0 unobserved |
| Listener fault accounting (F-02) | **Qualified** | 30/30 balanced rounds |
| Bounded shutdown | **Qualified** | Frozen test, `short-budget-unaccepted`, `race-delay` |
| Concurrent shutdown | **Qualified** | `concurrent-dispose`, `external-listener-stop` |

**Known limitations:**
- **Preview release** — API may evolve based on interoperability feedback
- **Windows-only qualification** — Native Linux formal qualification pending separate commission
- **F5 L3/L4 diagnostics** not run — separate harness-owned `SslStream` shutdown path
- **Bolt 6.x / PackStream 1.1** not supported
- **WebSocket/WSS transport** not implemented
- **Routing / clustering** not implemented (single-node backend)
- **Live client interoperability** not yet validated with Neo4j Browser or official drivers

## Project references

- [Bolt protocol documentation](https://neo4j.com/docs/bolt/current/)
- [Protocol compatibility research](docs/compatibility-target.md)
- [Legal and compatibility notes](docs/legal-and-compatibility.md)
- [Apache License 2.0](LICENSE)

## Contributions

Contributions, reproducible bug reports, and interoperability probes are welcome. Prefer original implementation, version-bounded claims, recorded evidence, and clear third-party license attribution.
