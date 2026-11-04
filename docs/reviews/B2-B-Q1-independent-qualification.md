# B2-B-Q1 — Bolt handshake qualification

Date: 2026-11-04 (Asia/Manila). Reviewer: Codex, current qualification session.

## 1. Verdict and owner gate

**REMEDIATION REQUIRED.** Finding **B2BQ1-F01 (MAJOR)** reproduces successful negotiation of
protocol zero in both legacy and Manifest v1 paths. Four reviewer assertions fail. No production
behavior was changed, and no remediation was attempted. Stop at the B2-B owner decision gate;
owner acceptance remains PENDING and B2-C is not authorized by this review.

Author independence is **UNVERIFIED**. This is executable technical qualification evidence,
not a claim of established author-independent qualification.

| Gate | Result | Evidence / qualification |
| --- | --- | --- |
| G1 — provenance | PASS | Exact entry, direct baseline parent, clean entry, additive 23-file diff, preservation checks below. |
| G2 — specification correctness | FAIL | F01 contradicts the protocol-zero invariant. Other exercised protocol cases pass. |
| G3 — adversarial testing | PASS technically; independence UNVERIFIED | 67 reviewer-owned cases executed; 63 pass and 4 expose F01. Author distinction is not established. |
| G4 — resource and state safety | FAIL overall | Exercised bounds and stream preservation pass; F01 publishes invalid successful state. |
| G5 — regression | PASS | 759 solution + 18 separate existing reviewer cases; all pass. |
| G6 — evidence integrity | PASS | Original production, tests and historical evidence preserved; new evidence retained separately with hashes. |

The initial probe run completed before the defect was classified. Further adversarial expansion
stopped after confirmation. Passing rows below establish only their exercised guarantees, not
universal conformance or acceptance.

## 2. Independence declaration

This session did not author or edit the reviewed implementation. However, that alone does not
identify the model or reviewer that produced the earlier implementation session. Git records
`BoltSharp Research Agent <agent@bolt-sharp.local>` as author/committer, which is not evidence
of a different model. The owner explicitly confirmed that the implementation model is unknown
and instructed proceeding with technical tests while recording independence as UNVERIFIED.
No model distinction, independent-human signoff, or original-session identity is asserted.
Fixture expectations here were derived from the official specification and public contracts;
producer helpers were not reused.

## 3. Reviewed identity, provenance and preservation

Canonical checkout: `D:\projects\bolt-sharp`, branch `main`. No worktree or branch was created.

| Object | Exact identity |
| --- | --- |
| Implementation | `ca36d36ee1b840b444180432e355832d56f5384f` |
| Implementation tree | `61485c1c37864a80c00692330958b04483e1204d` |
| Accepted B2-A baseline / sole implementation parent | `a46c916e7f6e72e315504e2d284921d406ee6c74` |
| Baseline tree | `3f8a0fce87f2ff2bede27910d9894303ceff65a0` |
| Reviewed `src` tree | `8a4567241be0f5ee294c6ac23b708cc5aea280fe` |
| Reviewed `tests` tree | `1bbbd901e7682b54544a5d90316d28bdb81ecbe1` |

Before reviewer writes, elevated `git status --porcelain=v1` returned no output. `git branch
--show-current` returned `main`; HEAD exactly matched the requested entry. Non-elevated Git
initially failed to inspect the worktree because of sandbox access; that was not treated as
repository drift. The elevated check resolved it before reviewer changes.

Executed `git merge-base --is-ancestor <baseline> <implementation>` returned 0; `git rev-parse
<implementation>^` equals the baseline. The [implementation diff](evidence/B2-B-Q1/implementation-diff.txt)
has exactly 23 `A` entries: ten handshake source files, eleven handshake test/helper files, and
two handoff documents. Excluding those four authorized path groups leaves an empty diff.
Existing B1/B2-A source/tests, `.csproj`, `.props`, solution, dependencies and earlier evidence
were not changed by implementation. The baseline tree is preserved as Git objects.

The [verifier](probes/B2-B-Q1/Verify-Evidence.ps1) repeats these checks and checks the reviewed
entry against the working tree for `src`, `tests`, shared properties, solution and historical
documents. Those diffs remain empty. The [full reviewed tree listing](evidence/B2-B-Q1/reviewed-tree.txt)
records every original blob identity. New review files stay outside the main solution.
No prior commit or record was amended, rebased, squashed or replaced.

Repository contracts read: [B2 task](../tasks/B2-bolt-integration.md),
[governing spec](../boltsharp-spec.md), [compatibility target](../compatibility-target.md),
[B2-B implementation record](B2-B-bolt-handshake.md),
[qualification handoff](B2-B-Q1-qualification-handoff.md), and
[B2-A acceptance](B2-A-A1-owner-acceptance.md). All ten handshake source files and all eleven
handshake test/helper files were inspected. Existing producer tests are supporting evidence,
not the oracle for new fixtures.

## 4. Specification conformance matrix

Primary authority retrieved on 2026-11-04:
[Neo4j Bolt handshake specification](https://neo4j.com/docs/bolt/current/bolt/handshake/).
Relevant headings are named below. The upstream raw AsciiDoc fetch through the web tool failed
with a cache miss; it is not claimed as a successfully examined second source. No real-driver
behavior was needed to resolve F01.

| Area | Independent expectation / result | Probe methods below |
| --- | --- | --- |
| Q1 identification / proposals | Magic, four big-endian proposals, preference, placeholders, rejection and ownership pass; zero-range success fails. | P01, P10, P14, P16 |
| Q2 representations | Concrete layouts, bounded same-major expansion, reserved-byte policy and ordering pass; selectable zero fails. | P01, P07, P16 |
| Q3 Manifest v1 | Marker positions, offer bytes, concrete selection, offered-version membership and capability subsets pass; offered zero fails. | P02–P04, P10, P15 |
| Q4 VarInt | Literal boundaries, truncation, overflow, exact consumption, fragmentation and documented non-minimal acceptance pass. | P05–P06 |
| Q5 limits | Count rejection before body/allocation; tested count/byte boundaries and read guards pass. | P08–P09 |
| Q6 failures / integrity | Typed malformed/truncated/protocol failures, propagated I/O errors and cancellation pass; invalid successful zero state fails. | P10, P12–P13, P15–P16 |
| Q7 pipelining | Exact handshake consumption; partial next header and two independently framed messages preserved. | P11 |
| Q8 honesty | Empty defaults and explicit test-version opt-in pass. Zero must still be excluded. Messaging compatibility is unestablished. | P14–P16 |

Source headings: “Bolt identification,” “Version negotiation,” “Bolt version 4.0,” “Bolt
version 4.3,” “VarInt,” and “Handshake Manifest v1.” This specification does not prescribe this
library's exception classes or error positions. Non-minimal VarInts, zero-entry offers and
reserved-bit rejection are evaluated against documented local policy where the specification
does not explicitly settle rejection behavior. Selecting the highest matching minor within a
range and deterministic offer ordering are local declared policies, not invented universal rules.

## 5. Reviewer probe inventory and mutation reasoning

Source: [QualificationProbes.cs](probes/B2-B-Q1/QualificationProbes.cs), namespace
`BoltSharp.Qualification.B2BQ1`. Project: [B2BQ1.Probes.csproj](probes/B2-B-Q1/B2BQ1.Probes.csproj).
The project references the unchanged core and the same test-package versions already used in
the repository. It is not added to the solution.

| ID | Method (unique suffix / prefix) | Discovered | Claim falsified / plausible defect detected |
| --- | --- | ---: | --- |
| P01 | `Legacy_preference_ranges_duplicates_and_major_isolation` | 14 | Seven wire scenarios on both paths; choosing server preference, including an out-of-range minor, or ignoring major fails. |
| P02 | `Manifest_marker_at_each_position_respects_earlier_legacy_match` | 2 | All four positions and earlier legacy ranges; globally prioritizing manifest fails. Gap-aware offer golden bytes catch merging unsupported minors. |
| P03 | `Literal_spec_manifest_example_and_zero_capabilities` | 2 | Exact official example plus zero/all subsets; wrong count/range/capability bytes fail. |
| P04 | `Capability_subsets_use_all_64_bits_and_reject_unoffered_bits` | 2 | High-bit, multibyte, full and empty subsets; 32-bit truncation or reversed subset mask fails. |
| P05 | `Varint_literal_vectors_exact_consumption_and_every_truncated_prefix` | 24 | Twelve vectors, both stream paths, writer/span decoder, every incomplete prefix; endian, continuation or read-ahead mutation fails. |
| P06 | `Varint_overflow_overlength_and_nonminimal_are_distinct` | 2 | Tenth-byte payload/continuation and eleven-byte input; removing overflow check or reading byte eleven fails. |
| P07 | `Version_wire_space_and_range_boundaries_are_exhaustively_consistent` | 1 | 65,536 concrete layouts, max/zero ranges, underflow, major isolation, ordering/equality; swapped byte layout fails. |
| P08 | `Offer_count_cap_and_byte_cap_reject_before_body_reads` | 1 | Counts 0/1/255/256/257, max UInt64 count, byte-cap −1/exact/+1; allocating from unvalidated count or delayed budget check fails. |
| P09 | `Negotiation_byte_cap_is_checked_before_each_field_read` | 2 | Legacy budgets 19/20/21 and manifest 25/26/27; reading beyond cap fails. |
| P10 | `Disconnect_at_every_handshake_prefix_and_malformed_transitions` | 2 | Every incomplete prefix through final capability continuation, malformed magic/selection/proposals, unoffered version/capabilities; treating EOF as success fails. |
| P11 | `Pipelined_frames_and_partial_next_header_remain_exact` | 2 | Legacy/manifest × one-byte/coalesced × partial header/two messages; buffering past selection fails. |
| P12 | `Read_write_and_flush_failures_publish_no_result_and_preserve_ownership` | 2 | Read faults before/after offer, partial writes and flush faults on both paths; swallowing transport failure or disposing caller stream fails. |
| P13 | `Cancellation_at_async_transitions_abandons_incomplete_state` | 5 | Cancellation at positions 0/4/20/24/25; dropping the token fails. No result may be published. |
| P14 | `Reused_configuration_has_no_cross_stream_state_and_default_is_honest` | 2 | Repeated legacy/manifest negotiations, zero supported versions, malformed proposals; stale result reuse or default advertisement fails. |
| P15 | `Zero_version_must_never_be_a_successful_manifest_selection` | 2 | **FAIL:** zero offered and accepted. Missing zero-version validation is detected. |
| P16 | `Range_containing_zero_must_not_produce_accepted_legacy_zero_response` | 2 | **FAIL:** zero wire rejection reported as success without closure. |
| | **Total** | **67** | **63 passed, 4 failed, 0 skipped.** |

Mutation reasoning identifies concrete changes the assertions would detect; production was
not mutated and no mutation score is claimed. Tests use literal hex and independent byte/radix
arithmetic, not production fixture generation or producer test helpers.

## 6. Golden vectors and derivation

All hex below is wire order. Official example vectors derive from the source headings in §4.
Additional values use radix-128 digit decomposition (continuation bit on every nonfinal digit)
or independent byte indexing.

| Meaning | Literal hex |
| --- | --- |
| Identification | `60 60 B0 17` |
| Concrete 4.1 | `00 00 01 04` |
| Range 5.8–5.6 | `00 02 08 05` |
| Specification manifest offer | `00 00 01 FF 02 00 02 08 05 00 04 04 04 09` |
| Specification selection | `00 00 07 05 08` |
| VarInt 0 / 1 / 127 / 128 | `00` / `01` / `7F` / `80 01` |
| VarInt 255 / 256 | `FF 01` / `80 02` |
| VarInt 16,383 / 16,384 | `FF 7F` / `80 80 01` |
| VarInt 2^32−1 / 2^32 | `FF FF FF FF 0F` / `80 80 80 80 10` |
| VarInt 2^64−1 | `FF FF FF FF FF FF FF FF FF 01` |
| Specification VarInt 1,851,775 | `FF 82 71` |
| Pipelined frames | `00 02 B0 01 00 00 00 01 A5 00 00` |

For example, 256 has radix-128 digits 0 and 2, yielding `80 02`; UInt64 maximum has nine
127 digits followed by 1. The tenth payload digit cannot exceed 1. Frame bytes are constructed
manually from chunk length, payload and terminator; decoding them demonstrates byte preservation,
not application-message compatibility.

## 7. Confirmed finding B2BQ1-F01

**Severity: MAJOR. Classification: protocol violation and invalid negotiated API state.**

Primary location: [BoltHandshakeOptions.cs](../../src/BoltSharp/Handshake/BoltHandshakeOptions.cs),
line 36, where configured versions are accepted without excluding `0.0`. Propagation sites:
[BoltHandshakeNegotiator.cs](../../src/BoltSharp/Handshake/BoltHandshakeNegotiator.cs), line 201
(legacy candidate accepted), line 216 (manifest offered-membership validation), and line 92
(manifest accepted result). [BoltHandshakeResult.cs](../../src/BoltSharp/Handshake/BoltHandshakeResult.cs),
lines 60 and 63 publish accepted state without a zero guard.

Violated requirement: the authoritative “Version negotiation” section states that zero always
represents “no protocol version.” The documented zero-response/closure contract also requires
`ShouldClose=true` for a rejection. Explicit test-version configuration cannot redefine that
wire sentinel. This is distinct from merely configuring a non-operational experimental version.

Precondition: a caller configures `new BoltProtocolVersion(0, 0)`. This is accepted by the public
constructor; no exclusion/precondition is documented. Defaults remain unaffected. No default
remote exploit, arbitrary memory exposure, or CRITICAL severity is claimed.

Reproduction command, from repository root:

```powershell
dotnet test docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj -c Debug --filter 'FullyQualifiedName~Zero_version_must_never|FullyQualifiedName~Range_containing_zero'
```

The command selects the four retained assertions; the executed evidence is the full suite in
[reviewer.txt](evidence/B2-B-Q1/reviewer.txt) and [reviewer.trx](evidence/B2-B-Q1/reviewer.trx).

**Manifest reproduction:** supported set `{0.0}`, Manifest enabled. Client preamble is magic,
`00 00 01 FF`, then three zero placeholders; selection is `00 00 00 00 00`.
Actual server offer: `00 00 01 FF 01 00 00 00 00 00`. Actual result on both paths:
`Accepted=true`, `SelectedVersion=0.0`, `ShouldClose=false`. Expected: zero must not be offered
or published as a successfully negotiated version. Rejecting the invalid configuration before
I/O is a suitable remediation; exact API exception policy needs to be defined by the implementer.

**Legacy reproduction:** supported set `{0.0}`, Manifest disabled. Client preamble is magic,
`00 01 01 00` (minor range 1 down to 0, major 0), then three zero placeholders.
Actual response: `00 00 00 00`; actual result: `Accepted=true`, `SelectedVersion=0.0`,
`ShouldClose=false`. Expected if configuration is accepted: rejected result, null selection,
and required caller closure. The same defect is reachable without sending a literal zero proposal.

Required remediation: exclude protocol zero from negotiable configuration and ensure no
zero-valued candidate/selection can publish accepted state, including through ranges. Add
implementer regression coverage for both schemes and sync/async paths; rerun these preserved
assertions after an authorized production change. No repair was performed here.

## 8. Resource-bound verification

Default manifest count cap is 256; byte cap is 4,096. P08 tests count cap−1/exact/+1 and UInt64
maximum. For counts 257 and UInt64 maximum the decoder consumes only marker plus count (6 and
14 bytes), then raises `BoltHandshakeFormatException`, with no body read. Per-thread allocated
byte observations are 1,176 and 1,992 respectively, including exception/test overhead. These
measurements corroborate the source's count check at `BoltManifestV1.cs:88` preceding the array
allocation at line 95; they are not a general zero-allocation proof.

Offer byte budgets at required−1/exact/+1 are tested for counts 0/1/255/256. P09 separately
tests the full server-side budget, including the 20-byte preamble. A legacy budget of 19 fails
after the four-byte magic, before requesting the 16-byte proposal field. A manifest budget of
25 consumes the first capability continuation and rejects before the next byte. Successful
paths consume exactly 20 or 26 bytes in those fixtures. Decoder read requests never exceed
16 bytes (offer decoder: four); VarInts request one byte and reject continuation/overflow after
ten bytes without reading an eleventh.

`MaxHandshakeBytes` limits received bytes per read operation, not outgoing offers.
`MaxManifestVersions` limits received range entries, not expanded concrete versions or the
trusted server configuration. Inspection bounds each range expansion to 256 entries; with
default count cap, `BoltManifestOffer.Versions` can enumerate up to 65,536 entries including
duplicates. The 256×256 maximum was not independently allocation-profiled. Custom extreme
configured caps are caller policy, not an established safe deployment setting. A byte cap
does not prevent a peer stalling indefinitely before the next byte; no internal timeout,
concurrency, total-traffic or rate guarantee is claimed.

## 9. Stream, failure and state verification

P11 leaves exactly the manually supplied frame suffix, including a lone first header byte,
unconsumed after negotiation. It then decodes two frames through the existing framing reader
on both paths with one-byte and coalesced input. The final capability `80 01` is split in the
one-byte case. Rejection writes exactly four zero bytes and requests caller closure in normal
nonzero-version configurations. Negotiation never disposes the supplied stream; the caller
must honor `ShouldClose` or close after exceptions.

P10 disconnects at every prefix length 0–25 of a 26-byte manifest handshake. Failures are typed
truncations; incomplete preambles emit no response. Wrong magic and reached malformed
proposals/selections produce format errors, while unoffered version/capability choices produce
protocol errors. Earlier matching proposals short-circuit later malformed proposals as documented.
No error-position property is promised by the handshake exception API, so framing's separate
position contract was not imposed on it.

P12 preserves the exact injected `IOException` identity through read, partial response write
and flush failures, with no result published and no stream disposal. Response bytes can be
partially emitted; rollback/recovery is not promised. P13 cancels at five async read transitions,
including incomplete selection/capabilities. These streams honor cancellation deterministically;
this is not proof of interrupting a transport that ignores tokens. Repeated distinct-stream
negotiations pass. F01 is the confirmed exception to valid-state publication.

## 10. Build and complete test accounting

Environment: Windows `10.0.26200`, `win-x64`; .NET SDK `10.0.401`; MSBuild
`18.9.11+e34a38d2a`; target `net8.0`; installed .NET 8 runtime `8.0.31`. The CLI host is
`10.0.12`, distinct from the test target/runtime. See [environment.txt](evidence/B2-B-Q1/environment.txt).

Executed commands from the canonical root (TRX logger/result-directory arguments are retained
in [commands.json](evidence/B2-B-Q1/commands.json) and the reviewer command above):

```powershell
dotnet build BoltSharp.slnx -c Debug
dotnet test BoltSharp.slnx --no-build -c Debug --logger 'trx;LogFileName=solution.trx' --results-directory docs/reviews/evidence/B2-B-Q1
dotnet test docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug --logger 'trx;LogFileName=previous-reviewer.trx' --results-directory docs/reviews/evidence/B2-B-Q1
dotnet test docs/reviews/probes/B2-B-Q1/B2BQ1.Probes.csproj -c Debug --logger 'trx;LogFileName=reviewer.trx' --results-directory docs/reviews/evidence/B2-B-Q1
```

| Executed set | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| B1 PackStream | 509 | 0 | 0 |
| B2-A framing | 54 | 0 | 0 |
| B2-A-Q1 probes | 41 | 0 | 0 |
| B2-B producer handshake | 155 | 0 | 0 |
| **Main solution** | **759** | **0** | **0** |
| B2-A-Q2 separate project | 18 | 0 | 0 |
| **All existing baseline** | **777** | **0** | **0** |
| **New B2-B-Q1 project** | **63** | **4** | **0** |

Solution build: **0 warnings, 0 errors** ([build.txt](evidence/B2-B-Q1/build.txt)). Both separate
projects compiled successfully with no emitted warnings/errors during their test commands.
The reviewer process exit is **1**, expected because real regression assertions fail; see
[reviewer-command.json](evidence/B2-B-Q1/reviewer-command.json). The
outer PowerShell pipeline returned success after printing that exit, and is not evidence of a
passing test process. Existing test processes exit 0.

[summary.json](evidence/B2-B-Q1/summary.json) derives counts from individual TRX result entries
and records selected assembly paths. Existing solution groups are classified by their fully
qualified test names; separate project assembly identities confirm Q2 and Q1 execution.
No count is inferred solely from the producer's handoff. Raw machine-readable results:
[solution.trx](evidence/B2-B-Q1/solution.trx),
[previous-reviewer.trx](evidence/B2-B-Q1/previous-reviewer.trx), and reviewer TRX linked above.

Reviewer tooling issues are separate from product findings: the first evidence-runner attempt
used a wrong relative root and an incompatible `Tee-Object -LiteralPath/-Append` parameter set.
It stopped before tests ran. Both were corrected before the retained runs; its isolated empty
artifact tree was removed. A pre-run gap-range expected literal was also corrected during probe
authoring. No executed reviewer test failure was reclassified or repaired to hide F01.

## 11. Supported guarantees, limitations and unsupported claims

For the exercised nonzero explicit configurations: ordered legacy/range choice, exact literal
manifest offer/selection bytes, subset validation across all 64 capability bits, bounded
VarInts, count/byte checks, deterministic failure classifications, caller stream ownership,
and exact pipelined-byte preservation have executable support. Default options advertise no
version and disable Manifest v1. Configuration of an experimental/test version does not prove
its messaging implementation. **Universal successful-negotiation validity is disproven by F01.**

Accepted boundaries remain: no rollback/resynchronization; no intrinsic timeout/flood policy;
non-minimal bounded VarInts accepted; custom budgets may reject otherwise valid inputs;
stream EOF/cancellation follows the supplied Stream contract. Empty version-list parsing is
not a successful negotiation. Transport failures may leave emitted response bytes.

Explicitly unsupported: author-independent signoff; Neo4j server, Browser or official driver
interoperability; live TCP/TLS/WebSocket qualification; HELLO, LOGON, queries, authentication or
application-state behavior; cross-platform operation; production security/deployment readiness;
package publication; B2-C/B3 completion. Evidence is Windows in-memory .NET 8 only; B0-A remains
PROVISIONAL. No owner acceptance is recorded.

## 12. Evidence binding and recommendation

All report links target tracked repository documents or new reviewer evidence. The new reviewer
project, scripts, this report, raw results and [SHA-256 manifest](evidence/B2-B-Q1/sha256.json)
are committed together as a reviewer-only commit after the unchanged implementation. This
file cannot embed its own commit hash; obtain the exact report/evidence commit with
`git log -1 --format=%H -- docs/reviews/B2-B-Q1-independent-qualification.md`; the final handoff
also gives that full hash. The evidence summary binds to implementation identities in §3.

At entry `main` was one commit ahead of local `origin/main`, whose recorded commit was
`a46c916e7f6e72e315504e2d284921d406ee6c74`. The reviewer-only commit adds one further local
commit relative to that tracking reference. A read-only `git ls-remote --heads origin main`
returned live remote tip `357df2d6836c4995a741b72bd442f09efa91756f`, which differs from the
local tracking reference. That object is absent locally (`git show`/ancestry checks exit 128),
so the live remote's ancestry/ahead-behind relationship is **UNKNOWN**. No fetch or reference
update was performed. See [remote-state.txt](evidence/B2-B-Q1/remote-state.txt). This does not
change the exact reviewed local entry; do not interpret local ahead-of-tracking status as a
verified relationship to the live remote. No push, publication, branch creation,
owner-acceptance write or implementation change was performed by this review.

Recommendation: withhold B2-B acceptance and request owner authorization for a narrowly scoped
F01 remediation task. Then rerun the preserved failing probes and all 777 existing tests on the
new exact implementation, review the changed validation policy, and resolve author independence
with an identified distinct reviewer before representing a later result as author-independent.
The owner must decide whether to authorize that remediation and arrange the independence
requirement. B2-C and B3 remain outside this task.
