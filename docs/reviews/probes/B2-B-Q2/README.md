# B2-B-Q2 independent requalification probes

Reviewer-owned net8.0 project for the independent requalification of the B2-B-R1 zero-version
remediation. Excluded from the main solution; production code, producer tests and historical
evidence are not modified.

From the canonical repository root:

```powershell
dotnet test docs/reviews/probes/B2-B-Q2/B2BQ2.Probes.csproj -c Debug
```

The probes use literal wire hex and independent radix/byte arithmetic; no production encoder or
producer test helper constructs a decoder input. `ReviewStream` is a reviewer-owned duplex stream
with separate input/output, fragment control, deterministic cancellation, injected read/write/flush
failures, and consumed/written/ownership observation.

Where a probe claims to detect `B2BQ1-F01`, the file implements the pre-remediation reference
algorithm locally (`OldLegacyWouldSelectZero`, `OfferBytes`) and asserts that the old outcome
differs from the current outcome. That makes the assertion a discriminator of the defect rather
than a restatement of the implementation. One probe invokes the internal result factory by
reflection to verify the fail-closed zero guard directly.

Coverage: configuration filtering (only-zero, mixed, ordering); offer-range exclusion; legacy
selection with/without matches and zero placeholders; range boundaries and highest-minor choice;
manifest advertisement, zero selection, capability subsets, and a major-0 offer; no-match four-zero
response and closure; sync/async parity; failure-state publication; cancellation at read
transitions; truncated prefixes; malformed typing; pipelined suffix preservation after success;
repeated-negotiation state isolation.
