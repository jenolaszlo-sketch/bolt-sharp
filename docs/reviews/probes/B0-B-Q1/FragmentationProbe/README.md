# B0-B-Q1 FragmentationProbe

Reviewer-only, standalone minimal TCP responder for the official `Neo4j.Driver` 6.4.0 driver.
It deliberately contains **no BoltSharp reference and no BoltSharp codec**: the wire helpers in
`Program.cs` are written from the published Bolt 5.8 / PackStream 1.0 specification only.

Purpose:

- test handshake-response fragmentation with identical concatenated bytes and different write
  segmentation (whole, 1-3, 2-2, 3-1, 1-1-1-1, with 0–3000 ms inter-segment delays), for both the
  legacy handshake and Manifest v1;
- test post-handshake framing boundaries (per-byte, chunk header/body/terminator splits, 100 ms
  header delay, two legal chunks);
- probe HELLO SUCCESS metadata requirements (server / connection_id / hints variants);
- probe LOGON FAILURE metadata classification (gql_status / neo4j_code / description /
  diagnostic_record variants, legacy `{code,message}`).

Every run starts a fresh listener on an OS-assigned loopback port and a fresh driver; the probe is
invoked once per case so cases never share driver state.

## Build and run

```powershell
dotnet build docs/reviews/probes/B0-B-Q1/FragmentationProbe/FragmentationProbe.csproj -c Release
dotnet run --project docs/reviews/probes/B0-B-Q1/FragmentationProbe/FragmentationProbe.csproj -c Release --no-build -- --case hs-1-3 --out docs/reviews/evidence/B0-B-Q1/fragmentation/hs-1-3.json
```

Exit codes: 0 = every run had its expected outcome, 2 = unknown case, 3 = a run produced an
unexpected outcome.

## Case catalog

| Group | Cases | Expected outcome (driver 6.4.0) |
| --- | --- | --- |
| Handshake segmentation | `hs-whole`, `hs-1-3`, `hs-2-2`, `hs-3-1`, `hs-1-1-1-1`, `hs-1-3-d{1,10,50,1000,3000}`, `hs-2-2-d10`, `hs-1-1-1-1-d10`, `hs-whole-d0-wait`, `hs-manifest-whole` | Only whole-response cases succeed; every split case fails with `ServiceUnavailableException` / `IOException: Unexpected end of stream when performing handshake, read only returned {1..3} bytes but expected 4 bytes.` |
| Post-handshake framing | `ph-whole`, `ph-perbyte`, `ph-chunk-header`, `ph-chunk-body`, `ph-terminator`, `ph-two-chunks`, `ph-header-slow` | All succeed (query value 42); framing tolerates every legal segmentation. |
| HELLO metadata | `hello-full`, `hello-empty`, `hello-server-only`, `hello-connection-id-only`, `hello-no-hints`, `hello-hints-only` | `hello-full`, `hello-no-hints`, `hello-server-only` succeed; `hello-empty`, `hello-connection-id-only`, `hello-hints-only` throw `System.NullReferenceException`. |
| LOGON FAILURE metadata | `logon-failure-full`, `-no-code`, `-no-gql`, `-empty`, `-old-code`, `-message-only`, `-code-gql-no-description`, `-neo4j-code-only` | With `neo4j_code` → `AuthenticationException` (`Code=Neo.ClientError.Security.Unauthorized`); without → `DatabaseException` (`Code=Neo.DatabaseError.General.UnknownError`); legacy `{code,message}` → `AuthenticationException` carrying the message text. |

Evidence for all cases is under `docs/reviews/evidence/B0-B-Q1/fragmentation/` with an aggregate
`fragmentation-summary.json`; 5 fresh-process repeats of the critical cases are under
`fragmentation-repeat/`.
