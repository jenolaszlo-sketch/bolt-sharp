# BoltSharp.DriverIntegrationTests

Opt-in, **test-only** scaffold for the official Neo4j .NET driver, created by **B2-C-01**.

- Package: `Neo4j.Driver` **6.4.0** (exact pin, no floating range).
- This project is **not part of `BoltSharp.slnx`**. The core solution restore/build stays free of
  the driver; `src/BoltSharp` never references `Neo4j.Driver`.
- Direct Bolt URI semantics only (`bolt://`, `bolt+s://`, `bolt+ssc://`); routing schemes
  (`neo4j://` / `neo4j+s://`) are detected and rejected because BoltSharp has no cluster/routing
  semantics.

## Build and test

```powershell
dotnet test tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj -c Debug
```

The test run exits `0`. The `Scaffold_*` tests execute; the `D01`–`D10` connectivity scenarios are
**gated (skipped)** with the reason:

```text
Requires B3 message/session engine and B5-compatible network transport.
```

A skipped test is **not** a passing compatibility test and must never be reported as one.

## Configuration (environment variables)

No credentials are hardcoded. A future B5 listener can be targeted without editing code:

| Variable | Default | Meaning |
| --- | --- | --- |
| `BOLTSHARP_DRIVER_URI` | `bolt://127.0.0.1:7687` | Direct endpoint (no routing scheme) |
| `BOLTSHARP_DRIVER_USER` | (empty) | Basic-auth principal; empty means no auth |
| `BOLTSHARP_DRIVER_PASSWORD` | (empty) | Basic-auth password; never logged |
| `BOLTSHARP_DRIVER_ENCRYPTION` | `none` | `none` / `required` (informational) |
| `BOLTSHARP_DRIVER_TIMEOUT_SECONDS` | `5` | Connection timeout |
| `BOLTSHARP_DRIVER_EXPECTED_PROTOCOL` | `5.8` | Provisionally expected revision (B0-A is provisional) |

## Activation prerequisites

D01–D10 can only be enabled once:

1. the B3 message/session engine implements `HELLO`/`LOGON`/`RUN`/`PULL`/`RESET`/`GOODBYE`, and
2. a B5-compatible TCP/TLS transport exposes the same protocol engine on a disposable loopback
   endpoint.

Do not fake `HELLO`, `LOGON`, `SUCCESS`, `RECORD` or graph results to make these tests pass.
