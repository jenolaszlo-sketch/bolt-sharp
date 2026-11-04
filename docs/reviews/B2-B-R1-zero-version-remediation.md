# B2-B-R1 — Zero-version negotiation remediation

**Status:** COMPLETE — remediation and evidence prepared; independent requalification PENDING
**Date:** 2026-11-04 (Asia/Manila)
**Task:** B2-B-R1 (authorized remediation of finding **B2BQ1-F01**, MAJOR)
**Canonical branch:** `main` (no additional worktree)
**Remediates:** [B2-B-Q1 independent qualification](B2-B-Q1-independent-qualification.md) §7, finding
**B2BQ1-F01**

This is a separate additive record. It does **not** modify or rewrite
`docs/reviews/B2-B-Q1-independent-qualification.md`, the B2-B implementation record, the
reviewer probe project, or any historical qualification evidence. No owner acceptance is
recorded, and **acceptance is not authorized by this assignment**.

---

## 1. Verdict and scope

Finding **B2BQ1-F01** is remediated: protocol `0.0` can no longer be configured as a supported
version, offered in a manifest, selected, or published as negotiated state, while a zero-filled
client proposal slot remains a valid placeholder. The four preserved reviewer assertions that
reproduced the finding now pass without modification or relaxation.

The change is narrowly scoped to the handshake validation path: three production files and one
new regression test file. No version ordering, capability, resource-limit, cancellation,
pipelining, ownership or encoding behaviour was changed.

| Object | Exact identity |
| --- | --- |
| Starting production commit | `ca36d36ee1b840b444180432e355832d56f5384f` |
| Production tree | `61485c1c37864a80c00692330958b04483e1204d` |
| Qualification evidence commit (entry parent) | `c99ae17d5a446989ea63098d7e368fabefd70166` |
| Qualification evidence tree | `89c2b178225fcd62e2ee28590973354af56956e1` |
| **Remediation commit** | **`b0bcadbf344ea0eef95cdc60702fd1bf8da620c8`** |
| **Remediation tree** | **`fe4159ac5e68b891fdbbd34f489c38fa84ce04af`** |
| Remediation `src` tree | `078d3ec7dc3d9ca414a208233f6a3859c09a0651` |
| Remediation `tests` tree | `0333c12b277f1a07425ae770a9412eea187a2e50` |
| Direct parent of the remediation commit | `c99ae17d5a446989ea63098d7e368fabefd70166` |

The remediation commit is purely additive relative to its parent: three modified source files
and one added test file. The final evidence-only commit that carries this record and the raw
evidence is recorded in the final handoff message of the task (this file is part of it, so it
cannot contain its own SHA).

## 2. Reproduced failure before remediation

The exact reviewer command was executed unmodified against production `ca36d36`:

```powershell
dotnet test docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj -c Debug --filter 'FullyQualifiedName~Zero_version_must_never|FullyQualifiedName~Range_containing_zero'
```

Result: **Failed 4, Passed 0, Skipped 0.** Observed state before remediation:

| Probe (sync and async) | Wire response | `Accepted` | `SelectedVersion` | `ShouldClose` |
| --- | --- | --- | --- | --- |
| `Zero_version_must_never_be_a_successful_manifest_selection` | `000001FF010000000000` | `True` | `0.0` | `False` |
| `Range_containing_zero_must_not_produce_accepted_legacy_zero_response` | `00000000` | `True` | `0.0` | `False` |

The manifest response advertised a single range `00 00 00 00` (a `0.0` offer) and then accepted a
`0.0` selection; the legacy path accepted a `0.0` candidate produced by expanding the client
range `00 01 01 00`. The full pre-remediation run is preserved in
[evidence/B2-B-Q1/reviewer.txt](evidence/B2-B-Q1/reviewer.txt) and
[evidence/B2-B-Q1/reviewer.trx](evidence/B2-B-Q1/reviewer.trx) (63 passed, 4 failed of 67).

## 3. Root cause and propagation sites

The Bolt handshake specification defines `0.0` as the wire sentinel for “no protocol version”.
The implementation treated it as an ordinary concrete version:

1. `BoltHandshakeOptions` constructor accepted `0.0` into `SupportedVersions`.
2. `BoltHandshakeNegotiator.Decide` selected any candidate contained in `_supported`, including
   `0.0` produced by expanding a client range.
3. `BoltHandshakeNegotiator.ValidateSelection` accepted a manifest selection contained in an
   offered range, including a `0.0` offer.
4. `BoltHandshakeResult.AcceptedLegacy`/`AcceptedManifest` published `accepted: true` and
   `shouldClose: false` without a zero guard.

This matches the four propagation sites named in the qualification report §7.

## 4. Remediation

| Site | Change |
| --- | --- |
| `BoltHandshakeOptions` constructor | `0.0` is excluded from `SupportedVersions` at the configuration boundary. An explicit `0.0` configuration is rejected there and can never be offered, selected or accepted. XML contracts document this. |
| `BoltHandshakeNegotiator.Decide` | A `0.0` candidate is skipped explicitly, so a client range that spans zero can never yield a zero selection. |
| `BoltHandshakeNegotiator.ValidateSelection` | A manifest selection of `0.0` raises `BoltHandshakeProtocolException` and publishes no result. |
| `BoltHandshakeResult` accepted factories | A fail-closed `GuardNegotiatedVersion` rejects a zero version before any accepted state is constructed. |

**Configuration-boundary policy decision.** The reviewer left the exact API exception policy to
the implementer and the preserved assertions require a *result* (not a construction exception)
from an explicit `0.0` configuration. The remediation therefore **excludes** `0.0` during
normalisation rather than throwing from the constructor. This makes `{0.0}` behave exactly like
an empty supported set: manifest is effectively unavailable (`SupportedVersions.Count == 0`) and
legacy negotiation writes `00 00 00 00` and requests closure. No public signature changed.

The result-factory guard is defence in depth: with both entry points closed it is not
independently reachable, and it fails closed if a future caller ever supplies zero.

## 5. Finding-to-fix-to-test traceability

| Requirement (R2 invariant) | Fix site | Regression test(s) |
| --- | --- | --- |
| `0.0` is never a valid negotiated protocol version | options ctor; `Decide`; `ValidateSelection`; result guard | all tests in `ZeroVersionNegotiationTests` |
| Explicit `0.0` configuration is rejected at the boundary | options ctor | `Configuration_WithOnlyZero_ExcludesItFromSupportedVersions`, `Configuration_WithZeroAndValidVersions_KeepsOnlyTheValidVersions`, `Negotiator_FromZeroOnlyConfiguration_OffersNoRanges` |
| Legacy no-match writes exactly four zero bytes and closes | `Decide` reject path (unchanged) + zero-skip | `Legacy_ZeroOnlyConfiguration_RangeContainingZero_RejectsAndCloses`, `Legacy_NoMatch_WritesExactlyFourZeroBytesAndCloses` |
| No failed negotiation returns `Accepted=true` or `ShouldClose=false` | `Decide`; `ValidateSelection`; result guard | `Legacy_ZeroOnlyConfiguration_*`, `Manifest_ZeroOnlyConfiguration_*`, `Manifest_ValidOffer_ZeroSelection_ThrowsProtocolWithoutPublishing` |
| Manifest must not advertise/select/accept `0.0` | options ctor; `ValidateSelection` | `Negotiator_FromZeroOnlyConfiguration_OffersNoRanges`, `Manifest_ZeroOnlyConfiguration_MarkerAndZeroSelection_RejectsAndCloses`, `Manifest_ValidOffer_ZeroSelection_ThrowsProtocolWithoutPublishing` |
| Zero proposal slot remains a valid placeholder | `Decide` skip preserved | `Legacy_ZeroPlaceholders_BeforeValidProposal_StillNegotiate`, `Legacy_RangeSpanningZero_SelectsHighestSupportedNonZero`, `Manifest_ZeroPlaceholdersAlongsideMarker_StillNegotiate` |
| Ordering/limits/cancellation/pipelining unchanged | no change to those paths | full 786-test solution suite; 67 preserved reviewer probes |

## 6. Regression coverage added (27 cases)

New file `tests/BoltSharp.Tests/Handshake/ZeroVersionNegotiationTests.cs`. Discovery counts
(2 per theory with sync/async inline data):

| R3 category | Tests | Count |
| --- | --- | ---: |
| 1. Explicit `0.0` server configuration | `Configuration_WithOnlyZero_ExcludesItFromSupportedVersions`, `Configuration_WithZeroAndValidVersions_KeepsOnlyTheValidVersions`, `Negotiator_FromZeroOnlyConfiguration_OffersNoRanges` | 3 |
| 2. Legacy sync and async | `Legacy_ZeroOnlyConfiguration_RangeContainingZero_RejectsAndCloses` (2), `Legacy_ZeroOnlyConfiguration_ConcreteZeroProposal_IsPlaceholderAndRejects` (2) | 4 |
| 3. Manifest sync and async | `Manifest_ZeroOnlyConfiguration_MarkerAndZeroSelection_RejectsAndCloses` (2), `Manifest_ValidOffer_ZeroSelection_ThrowsProtocolWithoutPublishing` (2) | 4 |
| 4. Zero placeholders alongside valid proposals | `Legacy_ZeroPlaceholders_BeforeValidProposal_StillNegotiate` (2), `Legacy_RangeSpanningZero_SelectsHighestSupportedNonZero` (2), `Manifest_ZeroPlaceholdersAlongsideMarker_StillNegotiate` (2) | 6 |
| 5. No-match response bytes and closure | `Legacy_NoMatch_WritesExactlyFourZeroBytesAndCloses` (2) | 2 |
| 6. Successful legitimate negotiation | `Legacy_ValidVersion_StillNegotiates` (2), `Manifest_ValidVersion_StillNegotiates` (2) | 4 |
| 7. Repeated attempts / acceptance-flag consistency | `RepeatedLegacyNegotiations_ZeroAttemptsNeverLeakAcceptedState` (2), `RepeatedManifestNegotiations_ZeroSelectionAlwaysThrowsAndValidSelectionSucceeds` (2) | 4 |
| | **Total** | **27** |

The four preserved reviewer assertions were **not** modified, renamed or weakened; their project
and source are byte-identical to `c99ae17` (see §7).

## 7. Preservation and narrow scope

Verified against the entry parent `c99ae17`:

```text
git diff --name-status c99ae17 b0bcadb
  M src/BoltSharp/Handshake/BoltHandshakeNegotiator.cs
  M src/BoltSharp/Handshake/BoltHandshakeOptions.cs
  M src/BoltSharp/Handshake/BoltHandshakeResult.cs
  A tests/BoltSharp.Tests/Handshake/ZeroVersionNegotiationTests.cs

git merge-base --is-ancestor c99ae17 b0bcadb   -> exit 0
git rev-parse "b0bcadb^"                       -> c99ae17d5a446989ea63098d7e368fabefd70166
git diff --exit-code b0bcadb -- docs/reviews/probes/B2-B-Q1 \
    docs/reviews/evidence/B2-B-Q1 docs/reviews/B2-B-Q1-independent-qualification.md
  -> exit 0 (empty)
```

No `.csproj`, `.props`, `.slnx`, dependency, or historical report changed. The preserved B2-B-Q1
reviewer probes, their evidence, and the B2-A-Q2 probes are untouched. No existing test was
modified. No public signature or wire encoding changed; existing golden vectors still hold.

## 8. Build and test results after remediation

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild
`18.9.11+e34a38d2a`; target `net8.0`; installed .NET 8 runtime `8.0.31`; CLI host `10.0.12`.
See [environment.txt](evidence/B2-B-R1/environment.txt) and
[identity.txt](evidence/B2-B-R1/identity.txt).

Commands were executed from the canonical root and captured by
[Run-Evidence.ps1](evidence/B2-B-R1/Run-Evidence.ps1):

```powershell
dotnet build BoltSharp.slnx -c Debug
dotnet test  BoltSharp.slnx --no-build -c Debug
dotnet test  BoltSharp.slnx --no-build -c Debug --filter "FullyQualifiedName~ZeroVersionNegotiationTests"
dotnet test  docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj -c Debug
dotnet test  docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug
```

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| B1 (PackStream + B1-R1 remediation) | 509 | 0 | 0 |
| B2-A framing | 54 | 0 | 0 |
| B2-A-Q1 probes | 41 | 0 | 0 |
| B2-B handshake | 155 | 0 | 0 |
| **New B2-B-R1 regression** | **27** | **0** | **0** |
| **Full solution** | **786** | **0** | **0** |
| Preserved B2-B-Q1 reviewer probes (separate project) | 67 | 0 | 0 |
| — of which the four F01 assertions | 4 | 0 | 0 |
| B2-A-Q2 reviewer probes (separate project) | 18 | 0 | 0 |

Solution build: **0 warnings, 0 errors** ([build.txt](evidence/B2-B-R1/build.txt)). Every test
process exited `0` ([commands.json](evidence/B2-B-R1/commands.json)). Counts are derived from
individual TRX result entries in [summary.json](evidence/B2-B-R1/summary.json); raw results are
[solution.trx](evidence/B2-B-R1/solution.trx), [regression.trx](evidence/B2-B-R1/regression.trx),
[reviewer.trx](evidence/B2-B-R1/reviewer.trx),
[reviewer-four.trx](evidence/B2-B-R1/reviewer-four.trx) and
[previous-reviewer.trx](evidence/B2-B-R1/previous-reviewer.trx).

Baseline reconciliation: the pre-remediation solution was 759 tests (509 + 54 + 41 + 155) and the
B2-B-Q1 reviewer project was 63 passing / 4 failing. After remediation the solution is 786
(+27 regression) and the reviewer project is 67 passing / 0 failing.

## 9. Evidence binding

All report links target tracked repository documents or new evidence under
`docs/reviews/evidence/B2-B-R1/`. The evidence is bound to remediation identities
`b0bcadbf…` / `fe4159ac…` in [summary.json](evidence/B2-B-R1/summary.json) and
[identity.txt](evidence/B2-B-R1/identity.txt). This file cannot embed its own commit hash; obtain
the evidence commit with
`git log -1 --format=%H -- docs/reviews/B2-B-R1-zero-version-remediation.md`; the final handoff
message also gives that full hash.

## 10. Repository safety

Canonical `main` only; no worktree was created. Two additive commits were made after
`c99ae17`: the remediation commit `b0bcadbf…` and the evidence-only commit referenced in §9.
No commit was amended, rebased, squashed, cherry-picked or replaced, and no history was
rewritten.

At entry `main` was ahead of the local tracking reference `refs/remotes/origin/main`
(`a46c916e7f6e72e315504e2d284921d406ee6c74`). The live remote tip observed by the B2-B-Q1 review
was `357df2d6836c4995a741b72bd442f09efa91756f`, whose ancestry is **UNRESOLVED** and whose object
is absent locally. **No fetch, push, force-push or reference update was performed**, so the
ahead/behind relationship to the live remote remains unknown. Do not interpret local
ahead-of-tracking status as a verified relationship to the live remote.

## 11. Remaining limitations and unresolved qualifications

- **Independent requalification is required.** This task is implementation and evidence
  preparation only. It is not acceptance, and no reviewer independence is claimed here.
- **Author independence remains UNVERIFIED.** The B2-B-Q1 review recorded that the
  implementation model is unknown; that limitation is unchanged by this remediation.
- **Configuration policy is exclusion, not exception.** `0.0` is silently excluded from
  `SupportedVersions`. If a future contract prefers a documented construction-time throw, that
  is a separate, explicit API decision; it would require reconciling the preserved reviewer
  probes, which assert a result rather than a constructor failure.
- **Defence-in-depth guard not independently reachable.** The result-factory zero guard cannot be
  triggered through the public API after the boundary filter; it is a fail-closed invariant.
- Accepted B2-B-Q1 limitations are otherwise unchanged: no rollback/resynchronisation after a
  handshake failure; no intrinsic timeout/flood policy; non-minimal bounded VarInts accepted;
  in-memory Windows/net8.0 evidence only; no Neo4j/driver interoperability, transport, messaging,
  authentication or packaging claims. B0-A remains PROVISIONAL.
- All other B2-B-Q1 gates (provenance, resource/state safety except F01, regression, evidence
  integrity) were PASS and are re-exercised here, but this record does not restate them as
  independently requalified.

## 12. Stop condition

Implementation and evidence preparation are complete. The four original failures are closed and
all required tests pass:

- full solution **786 / 0 / 0** (0 build warnings, 0 errors);
- preserved B2-B-Q1 reviewer probes **67 / 0 / 0** (the four F01 assertions now pass unmodified);
- new B2-B-R1 regression **27 / 0 / 0**;
- B2-A-Q2 reviewer probes **18 / 0 / 0**.

**READY FOR INDEPENDENT REQUALIFICATION** (B2-B-Q2), to be executed by a separately identified
reviewer. This record does **not** declare B2-B accepted, does not self-certify independent
review, and does not begin B2-C or B3.
