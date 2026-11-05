# B2-B-Q2 — Independent handshake remediation requalification

**Status:** COMPLETE — independent requalification delivered; owner acceptance PENDING
**Date:** 2026-11-05 (Asia/Manila)
**Task:** B2-B-Q2 (independent requalification of B2-B-R1, finding **B2BQ1-F01**)
**Canonical branch:** `main` (no additional worktree)
**Disposition:** **SUPPORTED WITH QUALIFICATIONS**

This is an independent reviewer record. It does not modify production code, producer tests, or
historical evidence, and it records no owner acceptance.

---

## 1. Scope and reviewer role

This review independently requalifies the remediation of finding **B2BQ1-F01** (MAJOR) recorded in
[B2-B-R1](B2-B-R1-zero-version-remediation.md), against the [B2-B-Q1 qualification](B2-B-Q1-independent-qualification.md)
and the official [Neo4j Bolt handshake specification](https://neo4j.com/docs/bolt/current/bolt/handshake/)
(retrieved 2026-11-05). The remediation commit and its evidence commit were treated as untrusted
inputs; identity, hashes and behaviour were re-derived rather than trusted.

The reviewer designed 46 fresh reviewer-owned cases (namespace `BoltSharp.Qualification.B2BQ2`),
including negative controls that implement the pre-remediation algorithm locally, and reproduced
every producer claim.

## 2. Independence declaration (Q2-07)

- **Reviewing model / session:** the current qualification session (`DeepSeek V4.1 Flash`,
  provider `nvidia`), a distinct session from the implementation and evidence sessions.
- **Role:** independent qualification reviewer; reviewer-owned probes authored here.
- **Participation in implementation:** none. This session did not author, edit or commit
  `src/BoltSharp/Handshake/*`, the B2-B-R1 tests, the B2-B-R1 record, or its evidence. Reviewer
  commits are additive and isolated to `docs/reviews/probes/B2-B-Q2/`,
  `docs/reviews/evidence/B2-B-Q2/` and this report.
- **Independence status: INDEPENDENCE UNVERIFIED.** The Git author identity
  `BoltSharp Research Agent <agent@bolt-sharp.local>` is shared across producer and reviewer
  commits and is not evidence of a distinct model or person, and no model/session identity of the
  original implementer is established. Model diversity alone is not treated as proof of
  independence. All findings below are therefore executable technical evidence, not a claim of
  established author-independent signoff.

## 3. Reviewed identity and integrity (Q2-01)

| Object | Exact identity | Verified |
| --- | --- | --- |
| Reviewed baseline | `c99ae17d5a446989ea63098d7e368fabefd70166` / tree `89c2b178225fcd62e2ee28590973354af56956e1` | yes |
| Reviewed remediation | `b0bcadbf344ea0eef95cdc60702fd1bf8da620c8` / tree `fe4159ac5e68b891fdbbd34f489c38fa84ce04af` | yes |
| Reviewed evidence commit | `70607f0e89fa0554f9731b1b7dcc49e97798372d` / tree `f319b359f375ea9f1fab57e33d130948d3eb0d5f` | yes |
| Production commit (context) | `ca36d36ee1b840b444180432e355832d56f5384f` / tree `61485c1c37864a80c00692330958b04483e1204d` | yes |
| Remediation `src` / `tests` trees | `078d3ec7dc3d9ca414a208233f6a3859c09a0651` / `0333c12b277f1a07425ae770a9412eea187a2e50` | yes |

Entry `HEAD` was exactly `70607f0…` on `main`; the working tree was clean before reviewer writes.
Ancestry confirmed: `b0bcadb^ == c99ae17`; `70607f0^ == b0bcadb`. No amend, rebase, reset,
cherry-pick or history rewrite was performed by the producer or by this review.

**Production change-set (verified, `git diff --name-status c99ae17 b0bcadb`):** exactly three
modified source files (`BoltHandshakeOptions.cs`, `BoltHandshakeNegotiator.cs`,
`BoltHandshakeResult.cs`) and one added test file (`ZeroVersionNegotiationTests.cs`). Excluding
those paths, the diff is empty. The evidence commit `70607f0` adds only
`docs/reviews/B2-B-R1-zero-version-remediation.md` and `docs/reviews/evidence/B2-B-R1/**`.

**Preservation (verified):**

- `git diff --exit-code b0bcadb -- docs/reviews/probes/B2-B-Q1 docs/reviews/evidence/B2-B-Q1 docs/reviews/B2-B-Q1-independent-qualification.md` → empty (exit 0).
- The original four-failure probes are byte-identical from `c99ae17` to `HEAD`
  (`git diff --exit-code c99ae17 HEAD -- docs/reviews/probes/B2-B-Q1/QualificationProbes.cs …` → empty).
- The historical `evidence/B2-B-Q1/reviewer.trx` independently re-parsed: **63 passed / 4 failed / 67 total**, the four failures being exactly `Zero_version_must_never_be_a_successful_manifest_selection` and
  `Range_containing_zero_must_not_produce_accepted_legacy_zero_response` (sync and async).
- No `.csproj`, `.props`, `.slnx`, dependency or solution membership change; `BoltSharp.slnx` still
  declares only the core and test projects.

**Evidence authenticity:** the B2-B-R1 SHA-256 manifest
([sha256.json](evidence/B2-B-R1/sha256.json)) was re-computed against all 18 listed files:
**18 checked, 0 missing, 0 mismatched**, with no unlisted files. Independently derived counts from
the B2-B-R1 TRX files match its `summary.json` exactly (786 / 27 / 67 / 4 / 18).

No identity or evidence mismatch was found. Proceeding was authorized.

## 4. Specification-to-implementation traceability (Q2-02)

| Specification statement (retrieved 2026-11-05) | Implementation behaviour | Probe evidence |
| --- | --- | --- |
| “a zero value (four zero bytes) always represents ‘no protocol version’”; for the server it “indicates no version match” | `Decide` skips `0` proposals and skips a `0.0` range candidate; config excludes `0.0`; `ValidateSelection` rejects `0.0`; accepted factories guard `0.0` | `Configuration_filters_protocol_zero_and_preserves_order`, `Offer_ranges_never_cover_protocol_zero`, `Legacy_zero_only_config_*`, `Manifest_zero_only_config_*` |
| “If no match is found, a zero value is returned followed by immediate closure of the connection by the server.” | Legacy reject writes exactly `00 00 00 00` and returns `ShouldClose=true`; no read-ahead | `Legacy_no_match_writes_four_zero_bytes_and_preserves_suffix` |
| “the first match should be selected” (client preference) | First proposal yielding a supported concrete version wins | `Legacy_preference_order_first_match_wins` |
| Bolt 4.3 ranges: “cannot span multiple major versions” | `BoltVersionRange.FromUInt32` rejects reserved-byte and major-spanning values | `Version_range_wire_space_boundaries_are_consistent`, `Malformed_input_stays_typed` |
| Manifest: server replies marker, `N`, version ranges, `CAPABILITIES`; client replies a concrete version + capability subset | Offer/selection golden bytes and subset enforcement; unoffered version and superset rejected with no result | `Manifest_spec_style_offer_and_selection_bytes`, `Manifest_capability_superset_and_unoffered_version_stay_protocol` |
| Manifest: “the client can pipeline that with the first Bolt message(s)” | Excess bytes remain unconsumed after the handshake | `Pipelined_suffix_preserved_after_manifest_success` |

## 5. Independent source review of the four remediation sites (Q2-02)

**`BoltHandshakeOptions` (lines 40–60).** The constructor skips `version == default` before
de-duplication, so `SupportedVersions` can never contain `0.0`. `SupportedVersions` is a
`ReadOnlyCollection` over a local list that is never exposed, so it cannot be mutated back.
`BoltHandshakeOptions` is `sealed`. **Result: no reachable configuration path yields a `0.0`
supported version.**

**`BoltHandshakeNegotiator.Decide` (lines 178–217).** Placeholder `proposal == 0` is skipped before
range decoding. For a decoded range, each candidate equal to `default` is skipped before the
`_supported` lookup. Independently, `_supported` is built only from the filtered
`options.SupportedVersions`, and `BoltVersionRange.FromVersions` groups only configured minors, so
a major-0 offer range cannot extend down to `0.0` unless `0.0` was configured (which it cannot be).
The `Negotiator` is `sealed` and `Options`/`OfferRanges` are `get`-only. **Result: no legacy
selection of `0.0` is reachable.**

**`BoltHandshakeNegotiator.ValidateSelection` (lines 219–250).** A `0.0` selection throws
`BoltHandshakeProtocolException` before the offered-membership check, so a major-0 offer cannot
accept `0.0`; a nonzero-version selection is unchanged. The same method is called on both the sync
and async paths after the selection is read. **Result: no manifest acceptance of `0.0` is
reachable, and no result is published.**

**`BoltHandshakeResult` accepted factories (lines 59–86).** Both `AcceptedLegacy` and
`AcceptedManifest` call `GuardNegotiatedVersion`, which throws `ArgumentOutOfRangeException` for
`0.0` before any accepted state is constructed. `Rejected()` publishes `accepted:false`,
`shouldClose:true`, `SelectedVersion:null`. The constructor is private; the only other factory is
`Rejected`. **Result: accepted state can never carry `0.0`, even if a future caller bypassed the
selection guards.** An independent reflection probe confirms the guard throws for `0.0`.

**Sync/async consistency and failure publication.** `Decide`, `ValidateSelection`, the options
filter and the result factories are scheme- and transport-agnostic; only the read/write I/O differs.
A parity probe compares `Accepted`, `ShouldClose`, `SelectedVersion`, `NegotiatedCapabilities`,
`Mode`, exact response bytes and consumed byte count across five representative scenarios, sync and
async. Every failure path either returns `Rejected` or throws before a result is published.

**Conclusion:** no public or internally reachable path lets `0.0` reach an accepted protocol state.

## 6. Configuration-policy qualification (Q2-03)

The producer excludes `0.0` during normalisation instead of throwing from the constructor.

1. **Protocol-correct:** yes. The specification defines zero as the “no version” sentinel for both
   peers; excluding it cannot remove any negotiable version, because `0.0` is by definition not a
   protocol version. Behaviours remain conformant.
2. **Consistent with documented contracts:** yes. The exclusion is documented in the
   `BoltHandshakeOptions` XML contract (lines 16–22, 67); no prior contract promised a throw for an
   invalid version, and `SupportedVersions` is documented as “may be empty”.
3. **Predictable for callers:** yes, with one caveat. A caller that explicitly configures `0.0`
   receives an empty supported set and a deterministic rejection (`00 00 00 00` + closure), never a
   successful `0.0`. The caveat is that the invalid entry is silently dropped rather than reported;
   this is recorded as observation **Q2-OBS1** (LOW), not a defect, because no documented contract
   is violated and the fail-closed outcome is safe.
4. **Safe when exclusion leaves no supported versions:** yes. `{0.0}` behaves exactly like an
   empty set: manifest is unavailable (`SupportedVersions.Count == 0`) and legacy negotiation
   rejects and requests closure. No partial or inconsistent state is produced.

Constructor rejection would be an alternative API policy, not a correctness improvement: it would
change an otherwise valid fail-closed path into a construction failure and would conflict with the
preserved reviewer probes, which assert a negotiated *result* rather than a constructor exception.
**Assessment: the exclusion policy is appropriate and is not a defect.**

## 7. Independent adversarial test matrix (Q2-04)

New reviewer project: [docs/reviews/probes/B2-B-Q2](probes/B2-B-Q2/) (`B2BQ2.Probes.csproj`,
excluded from the solution). **46 discovered cases, all pass** ([q2-probes.trx](evidence/B2-B-Q2/q2-probes.trx)).
Fixtures are literal hex/radix arithmetic; production encoders and producer test helpers are not
used to build decoder inputs.

| Method (cases) | Attack / falsification target |
| --- | ---: |
| `Configuration_filters_protocol_zero_and_preserves_order` | Only-zero, mixed, duplicate and `Create` configuration; zero never present; ordering preserved |
| `Offer_ranges_never_cover_protocol_zero` | Offer ranges for five configs never contain `0.0`; negative control: old `{0.0}` range did |
| `Legacy_zero_only_config_rejects_range_spanning_zero` | `00 01 01 00` range; reject 4 zero bytes, closure, 20 consumed; negative control: old algorithm selected `0.0` |
| `Legacy_zero_only_config_concrete_zero_placeholder_rejects` | Zero placeholder(s) only; reject |
| `Legacy_zero_placeholders_preceding_valid_proposal_negotiate` | Two zero slots then `5.8`; accepts `5.8`, suffix preserved |
| `Legacy_preference_order_first_match_wins` | Client preference order honoured both directions |
| `Legacy_range_selects_highest_supported_minor_and_skips_zero` | Range picks highest supported minor; major-0 range picks `0.1`, not `0.0` |
| `Legacy_no_match_writes_four_zero_bytes_and_preserves_suffix` | Exact 4-byte response, closure, 20 consumed, suffix intact |
| `Manifest_zero_only_config_does_not_advertise_or_accept` | No offer; reject; negative control: old offer included `0.0` |
| `Manifest_valid_offer_zero_selection_throws_protocol` | Valid offer bytes, then `0.0` selection → protocol exception, no result |
| `Manifest_major_zero_offer_still_rejects_zero_selection` | Offer `0.1` only; `0.0` selection rejected; offer bytes lack `0.0` |
| `Manifest_spec_style_offer_and_selection_bytes` | Specification offer/selection bytes, exact consumption |
| `Manifest_capability_superset_and_unoffered_version_stay_protocol` | Superset and unoffered version reject with no result |
| `Manifest_zero_placeholders_around_marker_negotiate` | Marker plus zero slots still negotiates |
| `Manifest_mixed_config_offers_only_valid_versions` | Mixed config offers compact valid range only |
| `Sync_and_async_are_equivalent_on_every_scenario` | Five scenarios compared field-by-field, byte-for-byte |
| `Failure_paths_never_publish_accepted_state` | Reject/protocol/truncation paths publish no accepted state |
| `Cancellation_abandons_incomplete_state` (5) | Cancellation at read transitions 0/4/20/24/25; no result |
| `Truncated_prefixes_raise_truncated_without_early_response` | Every prefix of a 26-byte handshake; typed truncation; no pre-preamble response |
| `Malformed_input_stays_typed` | Wrong magic, reserved byte and major-spanning range stay `FormatException` |
| `Pipelined_suffix_preserved_after_manifest_success` | Exact suffix preserved after success |
| `Repeated_negotiations_do_not_leak_state` | Alternate reject/accept five times on one negotiator |
| `Accepted_result_factory_guard_rejects_zero_by_reflection` | Directly invokes the internal factory guard |
| `Version_range_wire_space_boundaries_are_consistent` | Independent byte indexing, range min/max/contains, malformed ranges |

**Mutation reasoning.** Rather than restating the implementation, the probes that target `F01`
embed the pre-remediation reference algorithm and assert the old outcome differs from the observed
outcome (`OldLegacyWouldSelectZero`, `OfferBytes`). For example `Legacy_zero_only_config_rejects_range_spanning_zero`
asserts both that production now rejects and that the old algorithm would have selected `0.0`; the
probe therefore fails against the defective behaviour and passes only against the fix.

## 8. Reproduction and regression (Q2-05)

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild `18.9.11+e34a38d2a`;
target `net8.0`; .NET 8 runtime `8.0.31`; CLI host `10.0.12`
([environment.txt](evidence/B2-B-Q2/environment.txt)). Commands executed from the canonical root and
captured by [Run-Evidence.ps1](evidence/B2-B-Q2/Run-Evidence.ps1); every process exited `0`
([commands.json](evidence/B2-B-Q2/commands.json)). Build result: **0 warnings, 0 errors**
([build.txt](evidence/B2-B-Q2/build.txt)).

| Executed set | Passed | Failed | Skipped | Overlap note |
| --- | ---: | ---: | ---: | --- |
| Full solution (`BoltSharp.slnx`) | 786 | 0 | 0 | includes the 27 B2-B-R1 regressions |
| — B2-B-R1 regression subset | 27 | 0 | 0 | subset of solution |
| B2-B-Q1 reviewer probes | 67 | 0 | 0 | separate project |
| — the four original F01 assertions | 4 | 0 | 0 | subset of the 67 |
| B2-A-Q2 reviewer probes | 18 | 0 | 0 | separate project |
| **B2-B-Q2 reviewer probes** | **46** | **0** | **0** | separate project (new) |

No overlapping groups are added into a single “unique total”. The solution breakdown (independently
re-derived from `solution.trx`) is B1 PackStream 435 + B1-R1 remediation 74 + B2-A framing 54 +
B2-A-Q1 probes 41 + B2-B handshake 155 + B2-B-R1 regression 27 = 786, unchanged from the producer's
claim. The pre-remediation baseline of 759 (509 + 54 + 41 + 155) remains fully green; the delta is
exactly the 27 added regressions.

**Original four failures:** the four assertions are byte-identical to `c99ae17` and now pass
(`reviewer-four.trx`, 4/0/0). The historical baseline run records them as failing (4/67), so the
closure is a behaviour change, not a test change.

## 9. Wider B2-B regression assessment (Q2-06)

The production diff is confined to the four validation sites. The following previously qualified
behaviours were re-exercised on the remediation and are **demonstrably unchanged**:

- **Version preference / ranges:** first-match ordering and highest-supported-minor selection
  (`Legacy_preference_order_first_match_wins`, `Legacy_range_selects_highest_supported_minor_and_skips_zero`), plus the 63 nonzero B2-B-Q1 probes.
- **Manifest capability masks:** subset acceptance and superset/unoffered rejection
  (`Manifest_spec_style_offer_and_selection_bytes`, `Manifest_capability_superset_and_unoffered_version_stay_protocol`).
- **Limits:** the 155 producer handshake tests (count/byte budgets) remain green within the 786-test solution.
- **Cancellation:** deterministic cancellation with no published result (`Cancellation_abandons_incomplete_state`).
- **Partial/malformed input:** typed truncation and format errors (`Truncated_prefixes_raise_truncated_without_early_response`, `Malformed_input_stays_typed`).
- **Pipelining:** exact suffix preservation after success (`Pipelined_suffix_preserved_after_manifest_success`).
- **Connection-close:** `ShouldClose=true` exactly on legacy rejection; accepted results keep
  `ShouldClose=false`; manifest invalid selections throw instead of returning a result.

No demonstrated regression was found; no hypothetical risk is asserted as a defect. The only
behavioural deltas are the intended ones: configuration excludes `0.0`, range expansion cannot
select `0.0`, manifest zero selection is rejected, and accepted state cannot carry `0.0`.

## 10. Findings

| ID | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| B2BQ1-F01 | MAJOR | Explicit `0.0` was configured, offered, selected and published as an accepted protocol version | **CLOSED** — see §11 |
| Q2-OBS1 | LOW (observation) | An explicitly configured `0.0` is silently excluded rather than reported to the caller | Accepted as an appropriate fail-closed configuration policy (Q2-03); no contract violation, no functional consequence |
| Q2-OBS2 | INFO | The public codec types `BoltVersionRange.FromVersions` / `BoltManifestV1.WriteOffer` can still represent a `0.0` range as inert data | Not a negotiation defect; the negotiator never emits or accepts such data. Recorded as an accepted layer boundary |

No new defect was discovered. No CRITICAL/MAJOR/HIGH finding remains.

## 11. Closure assessment of B2BQ1-F01

The finding is fully closed on the exercised surface:

1. **Configuration:** `{0.0}` and `{0.0, …}` never yield a `0.0` supported version.
2. **Legacy:** a range spanning `0.0` and a concrete `0.0` placeholder both reject with exactly
   `00 00 00 00`, `Accepted=false`, `ShouldClose=true`, `SelectedVersion=null`.
3. **Manifest:** no `0.0` offer is emitted; a `0.0` selection raises
   `BoltHandshakeProtocolException` with no result; a valid nonzero offer/selection is unaffected.
4. **Publication:** accepted-result construction rejects `0.0` (independently confirmed by reflection).
5. **Placeholders:** zero-filled client proposal slots still negotiate normally.
6. **Parity/state:** sync and async agree; no failure path leaks accepted state.

The original four failing assertions pass without modification, and the fix is the sole cause of
that change.

## 12. Remaining qualifications

- **Independence is UNVERIFIED** (§2). This is a limitation of the review arrangement, not of the
  remediation. It does not invalidate the technical result but means the result is not established
  author-independent signoff.
- **Q2-OBS1 / Q2-OBS2** are accepted policy/scope observations, not defects.
- Accepted limitations inherited from B2-B-Q1 remain unchanged (no rollback/resynchronisation; no
  intrinsic timeout/flood policy; non-minimal bounded VarInts accepted; in-memory Windows/net8.0
  evidence only; no Neo4j/driver interoperability, transport, messaging, authentication, packaging,
  cross-platform or production-security claims; B0-A remains PROVISIONAL).

None of these qualifications invalidates the claim that `B2BQ1-F01` is closed and the existing B2-B
handshake guarantees are preserved on the exercised surface.

## 13. Final disposition

**SUPPORTED WITH QUALIFICATIONS.** The remediation of `B2BQ1-F01` is correct, complete on the
exercised surface, and free of demonstrated regression; the qualifications are the unverified
reviewer independence and the two accepted observations. The result is suitable to present to the
owner for a separate acceptance decision.

## 14. Evidence binding

Reviewer-owned probes: [docs/reviews/probes/B2-B-Q2](probes/B2-B-Q2/). Raw evidence:
[docs/reviews/evidence/B2-B-Q2](evidence/B2-B-Q2/), with a SHA-256 manifest
([sha256.json](evidence/B2-B-Q2/sha256.json)) over 20 files and machine-readable results
([summary.json](evidence/B2-B-Q2/summary.json)). This report and its evidence are committed
additively after `70607f0`. This file cannot embed its own commit hash; obtain the reviewer commit
with `git log -1 --format=%H -- docs/reviews/B2-B-Q2-independent-requalification.md`.

At entry, local `main` was ahead of the stale tracking reference `refs/remotes/origin/main`
(`a46c916e7f6e72e315504e2d284921d406ee6c74`); the live remote tip `357df2d…` has unresolved
ancestry and was not fetched, pushed or reconciled. No history was rewritten.

## 15. Stop condition

Independent requalification is delivered and evidence is prepared. This record does not self-approve
B2-B, does not begin B2-C or B3, and implements no newly discovered defect. Owner acceptance is a
separate decision.
