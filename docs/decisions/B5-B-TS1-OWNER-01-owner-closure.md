# B5-B-TS1-OWNER-01 — Owner Acceptance and Closure

**B5-B-TS1 — ACCEPTED WITH QUALIFICATIONS — CLOSED.**

Decision date: 2026-11-16 (Asia/Manila). This decision accepts the B5-B-TS1 test-only
remediation on the basis of the independent **SUPPORTED WITH QUALIFICATIONS**
[B5-B-TS1-Q1 decision](../reviews/B5-B-TS1-Q1-qualification.md).

| Finding | Formal owner disposition |
| --- | --- |
| B5-B-F01-R1-OWNER-F01 | **CLOSED BY VERIFIED TEST REMEDIATION.** The standalone and pipelined oracles address the terminal FAILURE test race within the retained Q-B5B-09 delivery boundary. |
| B5-B-F01-R1-OWNER-F02 | **CLOSED BY VERIFIED TEST REMEDIATION.** The multi-client test synchronizes on server-side lifecycle events before asserting counters. |

The [prior owner closure](B5-B-F01-R1-OWNER-01-owner-closure.md#20-abortive-close-observation-retained-and-owner-findings)
defines both findings. This decision closes those two test findings. The original closure of
Q-B5B-03 retains its prior scope.

## Repository identity and independence

| Object | Commit | Tree |
| --- | --- | --- |
| Owner entry HEAD and committed Q1 qualification | b196db8f0bc612b7f1ef3f78412480239c3f04db | d7815b8ad338610e8343aefa6e948359b52b8fc2 |
| Q1-qualified producer evidence commit | d8c28a9bed0af6305936b2b6e427ca1365bed757 | 9e7f07ca2664e44230b10b4b92c9f435fd58fdcd |
| TS1 implementation | 2e0b9da0b8920a2f6cdd7de9fa968891d779090e | 3fbdbebf21aacd545fd3384c289c649e8c7ab75b |
| Implementation base and prior owner closure | 17886a5e7f709ef1e01624e49c9d90b50808566c | e7d3be41483ab7aa22b51b27c619d7f211d8520b |
| Local origin/main | 79f7965a0e8e351baf2dd388218290804c31389a | 80bc7b4f3ec18341251ea4e04195d051489dacb5 |

Each row's commit has the next row's commit as its sole parent. At entry, main was clean,
0 behind and 4 ahead of local origin/main, with one worktree and zero stashes. The Q1 report,
preflight, and manifest identify the same producer and implementation commits and trees. The
qualified producer tree carries the exact three changed test blobs from the implementation.
Q1 independently inspected the contract and diffs, reproduced the baseline, ran fresh
campaigns, and exercised bounded negative controls in isolated archive copies. The owner
verification used the [implementation record](../reviews/B5-B-TS1-implementation.md),
[producer package](../reviews/evidence/B5-B-TS1/manifest.json),
[Q1 package](../reviews/evidence/B5-B-TS1-Q1/README.md), and
[B5-B owner acceptance](B5-B-OWNER-01-owner-acceptance.md).
The owner reconciled the committed results without repeating the stability campaigns.

## Acceptance matrix

| Criterion | Accepted result | Independent evidence |
| --- | --- | --- |
| Terminal refusal test race | Standalone and pipelined tests cover their respective delivery and terminality requirements under the characterized unread-tail abortive-close behavior. | [Q1 §§4–6](../reviews/B5-B-TS1-Q1-qualification.md#5-owner-f01-assessment), [baseline runs](../reviews/evidence/B5-B-TS1-Q1/09-baseline-reproduction.tsv) |
| Standalone FAILURE oracle | A single complete, byte-exact FAILURE, clean EOF, no second message, and connection retirement are required. This oracle is load-bearing. | [Q1 §6.1](../reviews/B5-B-TS1-Q1-qualification.md#61-standalone-oracle), [test inventory](../reviews/evidence/B5-B-TS1-Q1/13-test-inventory.txt) |
| Pipelined terminality | The event after write and flush confirms one response and DEFUNCT. Tests assert closed completion, exact accounting, zero provider preparation and no pipelined tail execution. They check FAILURE fields and absence of further responses when observed, with the narrow reset/abort allowance. | [Q1 §§5.3 and 6.2](../reviews/B5-B-TS1-Q1-qualification.md#53-what-the-combined-tests-establish) |
| Multi-client counter race | Probes subscribe before connection creation. Counter updates precede ConnectionFailed and ConnectionCompleted. Tests pin the handshake failure detail, healthy-client progress and final accounting. | [Q1 §§7–8](../reviews/B5-B-TS1-Q1-qualification.md#7-owner-f02-assessment), [campaign B](../reviews/evidence/B5-B-TS1-Q1/05-campaign-B-multi-client.tsv) |
| Negative controls | The combined suite detected all six injected defects M1–M6. The unmodified M0 control passed. | [Q1 §6.3](../reviews/B5-B-TS1-Q1-qualification.md#63-negative-controls), [control outcomes](../reviews/evidence/B5-B-TS1-Q1/10-mutations-summary.json) |
| Independent campaigns and regressions | A 1,000 + B 600 + C 36,810 + D 123 + E 420 = **38,953 executions**, with **0 failures, 0 skips, 0 timeouts**. The owner reconciled all 238 process summaries and exit codes against the raw logs and TSVs. | [Q1 §9](../reviews/B5-B-TS1-Q1-qualification.md#9-independent-campaigns), [campaign inventory](../reviews/evidence/B5-B-TS1-Q1/README.md#evidence-inventory) |
| Builds and scoped formatting | Production build passed with zero warnings or errors. Solution build passed with zero errors and two pre-existing xUnit2012 advisories. Scoped formatting of the three TS1 files exited 0. | [Q1 build and format transcript](../reviews/evidence/B5-B-TS1-Q1/03-builds-and-format.txt) |
| Production behavior and protected scope | TS1's implementation changes exactly three transport test/helper files. The producer evidence and Q1 commits are additive documentation/evidence commits. Production code, public APIs, project/package files, historical evidence and prior owner decisions are intact. | [Q1 preflight](../reviews/evidence/B5-B-TS1-Q1/01-preflight.txt), [owner verification](../reviews/evidence/B5-B-TS1-OWNER-01/verification.json) |

## Evidence integrity

- **Q1 package:** 25/25 unique entries match the SHA-256 and byte counts of their
  Git blobs. The tracked inventory comprises the report and 24 hashed evidence files, plus
  the manifest. The working package inventory agrees. Q1 adds exactly those 26 paths.
- **Producer package:** 12/12 match its exact working-tree convention, and 11/12 match Git
  blob bytes. The 19,563-byte LF blob of TransportTestSupport.cs deterministically reconstructs
  the 20,066-byte producer preimage by replacing each LF with CRLF. The result matches both
  the producer hash and the current raw file. Q1 records the checkout portability limits
  of 9/12 on a fresh Windows checkout with core.autocrlf=true and 11/12 on an LF checkout.
  [Q1 portability analysis](../reviews/evidence/B5-B-TS1-Q1/12-portability-analysis.txt).
- **Historical evidence:** 619/619 canonical raw working-tree hashes match across 17 manifests.
  The two legacy B3-B implementation-source manifests match 36/36, with 31 current working-tree
  matches and five historical blob matches at their manifest-declared implementation commits.
  The reviewed historical manifests have zero references to TS1's changed files.
  [Q1 integrity transcript](../reviews/evidence/B5-B-TS1-Q1/11-integrity-verification.txt).
- **Owner decisions:** All six decisions in the Q1 audit have identical blob IDs at the TS1
  base and Q1 commit and match the working checkout under Git's clean-filter semantics.
  The owner commit is additions-only and is verified against the exact reviewed Q1 parent.
  [Owner evidence and verifier](../reviews/evidence/B5-B-TS1-OWNER-01/README.md).

## Q1 findings and producer observations

| Finding | Owner disposition |
| --- | --- |
| B5-B-TS1-Q1-F01 (LOW) | **Accepted, non-blocking evidence portability limitation.** Preserve the producer package as committed. Future evidence packages must explicitly identify whether each hash covers Git blob bytes or exact working-tree bytes and document the byte source. This package uses Git blob bytes. |
| B5-B-TS1-Q1-F02 (LOW) | **Accepted as a bounded test qualification under Q-B5B-09.** The pipelined delivery oracle is conditional under the characterized abortive close. The standalone byte-exact FAILURE oracle must remain intact and retain its assertion strength. Release-grade graceful-close remains unqualified. |
| B5-B-TS1-Q1-F03 (INFO) | **Acknowledged.** The observer stays subscribed for the lifetime of the per-test server. Reuse of a server could retain stale observers. |
| B5-B-TS1-Q1-F04 (INFO) | **Acknowledged.** Terminal-signature correlation suffices in the one-connection pipelined tests. Event attribution in a future multi-connection variant would require additional correlation. |
| B5-B-TS1-Q1-F05 (INFO) | **Acknowledged.** ConnectionAborted broadens the socket outcome allowance alongside ConnectionReset. The narrow classifier, server assertions, and standalone delivery gate constrain the current coverage. |
| B5-B-TS1-N01 | **Independently justified.** The client may read the LOGON reply before the server reports its event. The signature filter excludes the late LOGON event. |
| B5-B-TS1-N02 | **Independently justified.** The pinned BoltHandshakeFormatException is a B2 handshake rejection. The corrected test comment and assertion describe that failure class. |

The informational observations require no remediation in this commission.
[Q1 findings and rationale](../reviews/B5-B-TS1-Q1-qualification.md#13-findings).

## Preserved production qualifications and scope

| Qualification | Disposition |
| --- | --- |
| Q-B5B-03 — Unsupported-command transport closure | **Remains CLOSED** under the prior owner closure. |
| Q-B5B-04 — Generic 50N42 refusal classification | **Remains OPEN** as an independent qualification. |
| Q-B5B-05 — Same-connection RESET recovery after IGNORED | **Remains OPEN** as an independent qualification. |
| Q-B5B-09 — Release-grade hardening | **Remains OPEN**, including abortive-close behavior and client-visible terminal FAILURE delivery with unread pipelined input. |
| All other inherited qualifications | **Unchanged.** The prior owner decisions remain binding. |

TS1 resolves test instability. Production abortive-close behavior remains unresolved. The
characterized FAILURE delivery limitation has not been accepted as release-grade production
behavior, and production release acceptance requires a separate decision. This commission's
commit is limited to this new owner record and its evidence package. Production source,
tests and helpers, project and package files, APIs, historical evidence, and previously
accepted decisions are outside the commit scope.
[Prior closure and binding qualifications](B5-B-F01-R1-OWNER-01-owner-closure.md#23-remaining-binding-qualifications).

## Recommended next commission

**Q-B5B-09-INV-01 — Bounded investigation of production graceful close and input drain.**
Assess terminal FAILURE delivery with unread pipelined input, TCP shutdown and close semantics,
bounded input draining, resource exhaustion and denial-of-service risks, cancellation and
shutdown behavior, cross-platform implications, and official-driver compatibility. The
investigation should establish feasible remediation options and their tradeoffs before
production implementation is authorized. Q-B5B-04 and Q-B5B-05 stay independent. This record
recommends the investigation for separate authorization.

The [owner evidence verifier](../reviews/evidence/B5-B-TS1-OWNER-01/README.md#verification)
checks committed blob hashes, complete inventory, the exact Q1 parent and tree, and the
additions-only commit scope. The owner commit and tree are obtained with
git log -1 --format="%H %T" at the owner commit and recorded in the handoff.
