# B3-A-Q1 falsification probe

Reviewer-only console probe for the independent qualification of the B3-A message codec. It
references only the production `BoltSharp` assembly under review and carries its own independent
PackStream 1.0 encoder **and** generic decoder, written from the published specification — no
producer test code, fixtures or evidence are referenced or modified.

Probe groups (85 probes; see `falsification-results.json` in the evidence folder):

- **A** — signature classification: mislabelled payloads, known-unsupported (BEGIN/COMMIT/ROLLBACK/
  ROUTE), unknown tags, graph tags as messages, empty payload, non-structures.
- **B** — arity, truncation, trailing bytes, invalid UTF-8.
- **C** — HELLO schema: required `user_agent`, `bolt_agent` shape, prohibited legacy fields, null
  optionals, routing value types (probe C3 records conformance finding F-01).
- **D** — LOGON schema: scheme requirement, scheme shapes, unknown-scheme preservation.
- **E** — RUN schema: arity, field types, prohibited legacy metadata, parameter preservation.
- **F** — PULL/DISCARD: required `n`, `-1` sentinel, documented `n/qid < -1` policy, authentic
  B0-B captures.
- **G** — TELEMETRY: type contract and the parse/processing boundary for the API value domain.
- **H** — RECORD/response encoding verified through the independent decoder and B0-B-qualified
  fixture bytes; IGNORED zero-field vs historical one-field discrepancy; FAILURE recursion bounds.
- **I** — authentication confidentiality with synthetic sentinel credentials (ToString, exception
  messages and stacks, oversized secrets, immutability cast).
- **J** — limits: oversized declared lengths rejected before allocation; decode-path depth bound.

Build and run:

```powershell
dotnet build docs/reviews/probes/B3-A-Q1/FalsificationProbe/FalsificationProbe.csproj -c Release
dotnet run --project docs/reviews/probes/B3-A-Q1/FalsificationProbe/FalsificationProbe.csproj -c Release --no-build -- docs/reviews/evidence/B3-A-Q1/falsification-probe
```

Exit codes: 0 = every probe observed the reviewer's predicted behavior (`PROBE-MISMATCH` count 0);
4 = at least one probe observed something else (the evidence set would be stale).
