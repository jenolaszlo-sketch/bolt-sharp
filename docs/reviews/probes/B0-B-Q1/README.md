# B0-B-Q1 reviewer probes

Isolated, additive reviewer tooling for the independent qualification of B0-B-DRV-INT-01
(`docs/reviews/B0-B-Q1-independent-qualification.md`). No production code, producer test, oracle or
historical evidence file is modified by any probe.

| Probe | What it establishes | Evidence |
| --- | --- | --- |
| [`FragmentationProbe`](FragmentationProbe/) | Standalone minimal TCP responder (no BoltSharp code) for handshake/framing fragmentation, HELLO metadata and FAILURE classification against the real driver. | `docs/reviews/evidence/B0-B-Q1/fragmentation/`, `fragmentation-repeat/`, `fragmentation-summary.json` |
| [`NegativeControls`](NegativeControls/) | Copies of producer assertions run against deliberately mutated server behaviour; measures assertion sensitivity and documents blind spots. Producer infrastructure is linked read-only. | `docs/reviews/evidence/B0-B-Q1/negative-controls/summary.json`, `negative-controls-run.log` |
| [`GraphDecode/decode-graph.ps1`](GraphDecode/decode-graph.ps1) | Independent PackStream 1.0 decode of the exact RECORD hex recorded in the producer's T04 evidence, checked against the producer fixture expectations (23 checks). | `docs/reviews/evidence/B0-B-Q1/graph-independent-decode.json` |
| `driver-source/` (evidence folder) | Upstream driver 6.4.0 source excerpt (`BoltHandshaker.ParseProtocolVersionResponse`) downloaded from tag `6.4.0` (commit `4ed413b01906c5d1682c62a3ee3c982cb28f99cd`, matching the package informational version). | `docs/reviews/evidence/B0-B-Q1/driver-source/BoltHandshaker.cs` |

Build/run commands are documented in each probe's README. SHA-256 hashes for all reviewer evidence
and probe sources are recorded in `docs/reviews/evidence/B0-B-Q1/manifest.json`.
