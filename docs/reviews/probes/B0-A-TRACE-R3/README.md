# B0-A-TRACE-R3 decisive probe host

Test-only host for the B0-A-TRACE-R3 connection-termination classification regression source.
It links the producer R3 regression tests
(`tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/ListenerClassificationR3RegressionTests.cs`)
against a swappable listener source, so the same assertions can be run against:

* the live remediated listener (default), and
* a byte-faithful pre-R3 listener snapshot, producing the before-fix negative control.

No production code under `src/BoltSharp` is referenced beyond the existing handshake/framing
contracts. This is not a production transport.

## Commands

Live (remediated) listener:

```powershell
dotnet test docs/reviews/probes/B0-A-TRACE-R3/R3.csproj -c Debug
```

Pre-R3 negative control (absolute or repo-rooted `ListenerSource`):

```powershell
dotnet test docs/reviews/probes/B0-A-TRACE-R3/R3.csproj -c Debug `
  -p:ListenerSource=D:\projects\bolt-sharp\docs\reviews\evidence\B0-A-TRACE-R3\controls\pre-r3-listener.cs
```

The pre-R3 snapshot does not expose the shared `ClassifyFramedReadFailure` helper; the
behavioural probes (reset/abort/EOF) are the decisive negative controls against that revision.
