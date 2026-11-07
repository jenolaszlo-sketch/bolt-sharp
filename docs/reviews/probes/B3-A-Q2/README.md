# B3-A-Q2 focused requalification probe

Reviewer-only console probe for the B3-A-Q2 requalification of the HELLO `bolt_agent` remediation
(B3-A-R1-IMPL-01, finding B3-A-Q1-F01). It references only the production `BoltSharp` assembly and
carries its **own** fresh, spec-derived PackStream encoder and generic decoder — independent of the
B3-A-Q1 probe, the producer test fixtures and the production writer.

The probe asserts the exact outcome (accepted, or a specific `BoltMessageErrorCategory`) for:

- seven F-01 closure cases (missing/null/non-map/empty/missing-`product`/null-`product`/wrong-typed-
  `product`);
- five positive compatibility cases (minimal valid agent, optional agent entries, routing,
  notifications with null optionals, complete field set plus unknown metadata);
- three historical-fixture analyses reproducing the Q1 probe's C3/C13/C14 inputs, with the fixture
  bytes independently decoded to establish that `bolt_agent` is absent;
- two confidentiality cases with synthetic sentinel values.

Exit codes: 0 = every observed outcome matched the reviewer's specification-derived expectation;
4 = at least one expectation was not met (the recorded expectation mismatches are the finding).

Build and run:

```powershell
dotnet build docs/reviews/probes/B3-A-Q2/ClosureProbe/ClosureProbe.csproj -c Release
dotnet run --project docs/reviews/probes/B3-A-Q2/ClosureProbe/ClosureProbe.csproj -c Release --no-build -- docs/reviews/evidence/B3-A-Q2/closure-probe
```
