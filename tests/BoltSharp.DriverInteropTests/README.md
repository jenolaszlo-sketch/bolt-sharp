# BoltSharp.DriverInteropTests

B5-B-IMPL-01 official-driver interoperability harness: the **unmodified official
`Neo4j.Driver`** driving the **real accepted B5-A TCP/TLS server** over loopback sockets.

- Package: `Neo4j.Driver` **6.4.0** (exact pin, no floating ranges) — the previously qualified
  official-driver baseline.
- This project is **not part of `BoltSharp.slnx`**, following the repository convention
  established by `BoltSharp.DriverIntegrationTests` (B2-C-01) and `BoltSharp.IntegrationTests`
  (B0-B-DRV-INT-01): the core solution restore/build stays free of the official driver and the
  accepted core-solution test baseline is preserved. `src/BoltSharp` never references the driver.
- Direct Bolt URI semantics only (`bolt://`, `bolt+s://`, `bolt+ssc://`); the routing scheme
  (`neo4j://`) is exercised only as a documented refused-capability probe.

## What the scenarios prove

Every scenario constructs the genuine driver through its documented public API (for example
`GraphDatabase.Driver(uri, AuthTokens.Basic(...))`), starts the real `BoltTcpServer` on a
loopback ephemeral port with a deterministic test credential verifier and the accepted B3-C
reference provider, and asserts on the driver's application-visible results plus the server's own
diagnostics. A passive test-only byte tap (`WireTapProxy`, which never rewrites traffic)
supplements the scenarios with sanitized wire captures. No raw request is hand-built and nothing
is injected into the driver's stream.

| Scenario group | Coverage |
| --- | --- |
| B5-B2 connectivity / handshake | `VerifyConnectivityAsync`, manifest v1 negotiation, conventional fallback, HELLO/LOGON shapes, authentication negative controls, close sequence |
| B5-B3 scalar query | `RETURN 42 AS answer` through the public result API, RUN extra capture, terminal `SUCCESS {}` exhaustion, home-database metadata behavior |
| B5-B4 graph query | typed `INode`/`IRelationship` materialization and the accepted golden RECORD bytes |
| B5-B5 parameters | typed parameter preservation, explicit null versus missing, query-like payloads |
| B5-B6 cursor lifecycle | multi-record results, fetch-size pagination through `has_more`, `DISCARD` on partial consumption, session disposal with an outstanding result, pooled connection reuse |
| B5-B7 TLS | `bolt+ssc://` self-signed, `bolt+s://` system-trust negative control, isolated CA trust-list arrangement, plaintext-vs-TLS negative control, invalid credentials over TLS |
| B5-B8 errors | unsupported-query refusal classification, live-driver IGNORED sequence (`B0 7E`), no automatic retry, failure isolation |
| B5-B9/B10 lifecycle | server shutdown, multi-client isolation, driver recovery, explicit/managed transactions, routing, explicit database selection |

## Build and test

```powershell
dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug
```

Evidence generation (writes one sanitized JSON document per scenario when the variable is set):

```powershell
$env:BOLTSHARP_B5B_EVIDENCE_DIR="$PWD\docs\reviews\evidence\B5-B-IMPL-01"
dotnet test tests/BoltSharp.DriverInteropTests/BoltSharp.DriverInteropTests.csproj -c Debug
```

The evidence writer refuses to write any document containing the deterministic credential
sentinel. No certificate, key, PFX or real credential is ever written to disk: TLS certificates
are generated in memory per test (self-signed or signed by an in-memory test CA), and
`bolt+s://` is validated against the operating system trust store exactly as the driver documents
— no machine trust store is modified.

## Known refusal boundaries (documented, not claimed as support)

Explicit transactions (`BEGIN`), managed transaction callbacks, `ROUTE` and explicit database
selection are refused. See `docs/reviews/B5-B-IMPL-01.md` for finding `B5-B-IMPL-F01` (the
transport closes at the decode boundary without the B3-B documented `08N06` FAILURE) and the
capability matrix.
