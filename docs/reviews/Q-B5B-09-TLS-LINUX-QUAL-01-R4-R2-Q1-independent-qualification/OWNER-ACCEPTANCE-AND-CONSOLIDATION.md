# Owner Acceptance, Consolidation and F5 Diagnostic Entry Preparation

Reviewed subject `784cc3365cce16afdeec189e4d1349e4b844f250` (tree `13ac2e99…`).
Working commit `9834900bcf39bd6d5bab339e0805642acf11ba65` (tree `8a7043a…`).
Production base `36d7b69b…`, pin `7429c6bb…`. Identity `jenol <jenolaszlo-sketch@users.noreply.github.com>`.

# 1. OWNER ACCEPTANCE RECORD

| Subject | Status |
|---|---|
| F1–F4 | **Accepted** — existing acceptance preserved |
| F6 | **Accepted with qualifications** — see R4-R2-Q1 record |
| Windows qualification | **Accepted within its established scope** |
| F5 | **OPEN** — outstanding obligations |
| Native-Linux qualification | **OUTSTANDING** |
| Release readiness | **NOT ESTABLISHED** |

**Correction of the contradictory statement.** The R4-R2 report's phrase "F6
remediated; all findings F1–F6 now closed" is **incorrect**: F5 was and remains
open. The authoritative finding status is the matrix in §5. No historical report
or evidence has been rewritten.

**Clarification 1 (D9 labelling).** In the R4-R1 producer control, D9 was
"duplicate receipts on every attempt". In the R4-R2-Q1 independent control, D9
was re-used for "empty receipt journal". Both behaviours were in fact exercised:
duplicate-on-every-attempt produced 3 errors (one per attempt) in both the
producer control and independently (see `adversarial-f6-independent.txt`, D9
entry in that run is the empty-journal case and the duplicate-every-attempt case
appears as X-series ordering/duplicate coverage); the empty-journal case produces
1 error, "peer receipt journal record missing". No material defect results. F6 is
not reopened.

**Clarification 2 (non-vacuity).** A low error count is *not* by itself evidence
of intended-reason rejection. The independent control therefore recorded, for
every negative fixture, the **matching error text**, not merely the count. See
`adversarial-f6-independent.txt`: every negative case names the mutated object in
its message ("exactly one peer-receipt record is required, found N", "peer
receipt journal record missing", "a receipt record in this journal belongs to
attempt X cell Y", "peer receipt is associated with attempt X"). The vacuity check
is the separate observation that no fixture produced unrelated missing-summary
errors.

# 2. INITIAL AND FINAL REPOSITORY INVENTORY

| Item | Initial (this commission) | Final |
|---|---|---|
| Local HEAD | `9834900` (tree `8a7043a`) | unchanged |
| Branch | `main` | `main` |
| Worktrees | 1 — `D:/projects/bolt-sharp`, clean | 1, clean |
| Local vs remote `main` | local 44 ahead, remote 0 ahead | unchanged |
| Qualification branch | `56cd357`, in sync with its origin | unchanged |
| Stashes | 1 (`stash@{0}`, 2 files) | 1, preserved |
| Tags | none | 3 backup tags added (below) |
| Author/committer | `jenol <…>` | verified, unchanged |

Remotes present: `origin`, `origin/feature/protocol-spec`, `origin/main`
(`79f7965`), `origin/qualification/q-b5b-09-tls-linux-qual-01`.

**Unpublished set review (origin/main..HEAD).** 749 files: 735 under `docs/`,
6 under `src/`, 7 under `tests/`, 1 under `.github/`. The `src`/`tests` changes
are the previously accepted plaintext work: `git diff 7429c6b..HEAD -- src\|tests`
is **EMPTY**, so HEAD's production and frozen-test content is byte-identical to
the frozen pin — the `src`/`tests` entries in the unpublished set exist only
because `origin/main` (`79f7965`) predates the pin. No `bin/`/`obj/` artefacts.
No appsettings or `.env` files. Build 0 errors; tests **1257 passed / 0 failed**;
`dotnet pack` succeeds. Preserved R4, R4-R1 and R4-R2 evidence each re-verify
`VERIFIER: PASS`.

# 3. PRESERVATION AND RECOVERABILITY EVIDENCE

Recoverable refs created (recoverable inventory; no new permanent branches, no
worktrees, stash preserved):

- `consolidation-backup/main-9834900`
- `consolidation-backup/qualification-branch-56cd357`
- `pre-consolidation/head-9834900`

Stash `stash@{0}` confirmed recoverable: contains
`.github/workflows/q-b5b-09-tls-linux-qual-01.yml` and
`docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/run-campaign.py`; its parent object is
`6b8ddba`, an ancestor of `main`, so parent objects are reachable and the file
contents are recoverable from the stash commit.

All three preserved evidence packages and the frozen pins
(`7429c6bb…`, `36d7b69b…`) remain intact and unmodified.

# 4. PUBLICATION GATE — BLOCKED

Gates checked and their outcome:

| Gate | Result |
|---|---|
| Build clean | PASS (0 errors) |
| Offline test suite | PASS (1257) |
| Packaging validation | PASS (local artefact only) |
| Evidence verification | PASS (R4, R4-R1, R4-R2) |
| Frozen scope `7429c6b..HEAD -- src\|tests` | EMPTY |
| Attribution | verified `jenol`; no AI authorship |

**CI / release automation check.** The only workflow is
`.github/workflows/q-b5b-09-tls-linux-qual-01.yml`. Its `on:` block is
`workflow_dispatch` **only** — the single occurrence of "push" in the file is
inside a design-constraint comment at line 18. `permissions: contents: read`, and
the header records `persist-credentials: false`. No `dotnet nuget push`, no
`gh release`, no `softprops/*`, no `actions/create-release`, no `workflow_run`,
no `pull_request`, no `pack` automation anywhere in `.github/`. **Therefore
pushing `main` would not trigger any workflow, publish a NuGet package, or create
a release.**

**BLOCKER — accidental secret.** `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/
results/exec-05-44889075318/cert.key` is a genuine, non-redacted RSA private key
(`-----BEGIN PRIVATE KEY-----`), introduced by commit `d088e2e` and present at
HEAD. It is in the unpublished set, so the remote has never received it — but
because it sits inside the commit chain, a push of `main` would publish it.

I did **not** push, and did not rewrite history to purge it, because history
rewriting is explicitly outside my authorization ("No unauthorized history
rewriting", "No force-push"). This requires an owner decision.

# 5. CORRECTED FINDING-STATUS MATRIX

| Finding | Status |
|---|---|
| F1 L7 journal↔receipt binding | Qualified (accepted) |
| F2 L5 plaintext > 0 | Qualified (accepted) |
| F3 Fixture-count erratum | Qualified (accepted) |
| F4 Provenance documentation | Qualified (accepted) |
| F6 Receipt uniqueness | Qualified with qualifications (owner accepted) |
| F5 Native-Linux readiness | **OPEN** |
| E1 T5 exact-1441 deviation | Preserved as a documented qualification |
| Native-Linux qualification | Outstanding |
| Release readiness | Not established |

# 6. F5 DIAGNOSTIC COMMISSION PROPOSAL

**The prior causal explanation must not be treated as established.** Independently
observed, on the *same* profile (ordinal 1, 36864 preceding bytes), L3 attempt 1:

| Field | Windows (qualified) | WSL2 (precursor-not-observed) |
|---|---|---|
| candidate send buffer requested / effective | 2048 / **2048** | 2048 / **4608** |
| peer rcvbuf requested | 2048 | 2048 |
| fixture bytes | **5120** | **2048** |
| fixture_path_saturated | true | **true** |
| shutdown_pending / ms | **true** / 21.6 | **false** / 0 |
| elapsed_ms | 320.4 | **0.207** |
| zero-window peer reached `parked` | yes | **no (L3); yes (L4)** |

So the sender buffer is *already* reduced, the fixture path *was* saturated, and
close_notify still completed immediately — which does not fit "262144 bytes
against a 4 MB buffer". The binding constraint is more likely buffer-capacity
arithmetic among the **effective** send buffer, the **effective** peer receive
buffer and the **fixture payload size**.

**Diagnostic plan (establish mechanism before any production change):** record
requested *and* effective send and receive buffer sizes per attempt; measure TCP
window scale and advertised window; sample kernel send-queue occupancy
(`/proc/net/tcp` tx_queue) repeatedly; correlate fixture payload size with
saturation; instrument peer parking and synchronization; trace TLS close_notify
progression; distinguish shutdown-completion from pending-state observation;
compare Windows, WSL2 and native Linux; repeat each attempt ≥3 times for
reproducibility.

**Native-Linux `queue > 0` oracle controls.**
- *Positive:* a terminal write observably stuck (peer receive window closed, peer
  not reading) must exhibit non-zero tx_queue at the sample.
- *Negative:* a terminal write that completes must exhibit tx_queue zero at the
  sample **and** the attempt must be recorded as non-qualifying — never as passed.

**O1–O4 disposition.**
- O1 (choose sender-shrink vs profile extension) — **not decidable**; do not
  assume either is correct. Deferred until diagnostics establish the mechanism.
- O2 (exercise the native-Linux `queue > 0` oracle) — **outstanding**.
- O3 (run the full campaign on native Linux and verify) — **outstanding**.
- O4 (register the platform-coupling note) — **substantially already satisfied**:
  `campaign-plan.json` already carries a profile-4 note naming native-Linux.
  Restate as closed-pending-verification.

**Native-Linux prerequisite — EXPLICIT.** No authorized native-Linux environment
is currently available. WSL2 must **not** be substituted: it reports
`platform = Unix 6.6.114.1` with `wsl = true`, so `IsNativeLinux()` is false and
the `queue > 0` oracle is bypassed. A native Linux host is a hard prerequisite,
with .NET 10 and python3, and readable `/proc/net/tcp`.

# 7. CONSOLIDATION DISPOSITION

**C — CONSOLIDATION BLOCKED — REMEDIATION REQUIRED** (publication only).

Everything else is complete: preservation refs in place, stash recoverable,
build/test/packaging/evidence gates passed, CI confirmed non-auto-triggering.
Publication of `main` is blocked solely on the committed private key, which
requires an owner decision on remediation. The qualification branch has **not**
been integrated and the stash has **not** been applied — both correctly deferred,
since neither should proceed while publication is blocked.

**Stash disposition (assessed, not applied).** `run-campaign.py` and the campaign
workflow are both already present on `main` in equivalent form (the workflow is at
`.github/workflows/q-b5b-09-tls-linux-qual-01.yml`), so the stash is
**superseded on `main`**; retain it untouched as protected per owner instruction.

# 8. ROADMAP AND MILESTONE ASSESSMENT

- Implementation completeness: the qualified production subjects are frozen at
  `7429c6bb` and byte-identical at HEAD.
- Qualification completeness: Windows **complete and accepted**; native-Linux
  **outstanding**.
- Release readiness: **blocked** on F5/native-Linux and on the committed secret.
- Shortest defensible path to the next stable NuGet release: (1) owner decision on
  the private key and its purge-from-history; (2) republish `main`; (3) F5
  diagnostic commission on a native Linux host; (4) F5 remediation if the
  diagnostic warrants it, registered in `campaign-plan.json`; (5) native-Linux
  qualification campaign; (6) then NuGet publication and tagging — both still
  unauthorized here.

# 9. REMAINING RELEASE BLOCKERS

1. **Committed RSA private key** `results/exec-05-44889075318/cert.key`
   (introduced by `d088e2e`) — must be purged from the unpublished chain before
   publication; requires owner authorization for history rewriting.
2. F5 diagnostic commission unexecuted; no authorized native-Linux environment.
3. Native-Linux qualification outstanding.
4. NuGet publication and release tagging unauthorized.

# 10. RECOMMENDED NEXT ACTION

Owner decision on the private key: either authorize a history purge
(`git filter-repo`/`filter-branch` plus a force-push of the unpublished range,
which I have not performed), or authorize rotating the key and accepting its
presence as historical evidence. **Until then, `main` must not be pushed.** Once
resolved, re-run §4's gates and publish, then commission F5 diagnostics on a
native Linux host.

Stop at the owner decision gate. No F5 execution, no production remediation, no
NuGet publication, no release tagging, no force-push, no history rewrite.