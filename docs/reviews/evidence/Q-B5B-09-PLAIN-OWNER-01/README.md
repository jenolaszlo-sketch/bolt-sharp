# Q-B5B-09-PLAIN-OWNER-01 evidence

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

**Disposition:** PLAINTEXT REMEDIATION — ACCEPTED WITH QUALIFICATIONS — CLOSED.
Parent `Q-B5B-09` remains OPEN.

## Contents

| File | Description |
|---|---|
| `acceptance-matrix.md` | Requirement acceptance, finding closures, Q2 qualifications, typed-linger record, regression acceptance, reconciliation summary |
| `finding-closure-register.md` | Q1 finding closures mapped to both independent Q2 evidence packages |
| `qualification-ledger.md` | Updated remaining qualification ledger and outstanding release gates |
| `observer-isolation-backlog.md` | Deferred `Q-B5B-09-PLAIN-Q2-F01` backlog item |
| `next-commission-recommendation.md` | Recommended next commission (`Q-B5B-05`) with prerequisites |
| `q2-reconciliation.md` | Reconciliation of the two parallel Q2 qualifications, canonical acceptance basis, discrepancy register D-01 … D-11 |
| `verification.json` | Exact verified identities, ancestry and re-verified Q2 evidence results |
| `audit-owner-inputs.py` | Recomputes the verification results from the pinned commits |
| `extract-q2-packages.py` | Materializes both Q2 packages byte-exactly from their pinned commits |
| `manifest.json` | SHA-256 and byte counts for every package file (except itself) |
| `verify-evidence.py` | Self-verifying evidence verifier |
| `q2-session-a/` | First independent Q2 qualification (`96a1a3c`), preserved byte-exactly |
| `q2-session-b/` | Parallel independent Q2 qualification (`3c2144a`), preserved byte-exactly |

## Why both Q2 packages are preserved

The parallel Q2 qualification at `3c2144a` modified 13 files of the package committed at
`96a1a3c` in place and added 5 more, so the directory
`docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/` at HEAD is a merge of two sessions rather than
either session's package, and session A's overwritten artifacts survive only inside commit
`96a1a3c`.

Both packages are therefore materialized here byte-exactly, sourced only from
`git cat-file blob` payload bytes at the two pinned commits. Each restored copy carries a
`PROVENANCE.md` with the per-file Git blob ID, byte count and SHA-256. The merged package
directory was not modified, and no history was rewritten.

## Verify

```text
python docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/verify-evidence.py
```

The verifier works exclusively over Git blob payloads, so it is independent of checkout
line-ending normalization. It checks:

1. The owner decision's sole parent, tree and additions-only scope.
2. Every package file against the recorded SHA-256 and byte count.
3. Both preserved Q2 packages against their pinned commits, file by file, over Git blobs.
4. Session A's manifest entries are re-derived over Git blobs: the manifest holds 48
   entries — 47 package content blobs plus the qualification report — of which 47 (46
   content blobs plus the report) are independently re-derived, with `verify-evidence.py`
   skipped as self-referential. This reproduces the integrity that the package's own
   verifier cannot reproduce in a fresh Windows checkout (discrepancy D-04).
5. The rollback recovery provenance against the pre-rollback cleanup inventory.
6. The six TRX campaign counters.
7. The build and format logs (two xUnit2012 warnings, eight pre-existing whitespace
   findings).
8. The nine negative-control transcripts and their TRX files.
9. The R1 source and evidence blob identities, and the historical evidence and decision
   integrity.
10. The reconciliation claims: ancestry, package-collision state, disposition agreement
    between the two qualification reports, and the discrepancy register.
11. The live repository invariants at verification time (branch, cleanliness, worktrees,
    stashes, divergence). These are enforced only when the verifier runs in the primary
    working tree; in a linked worktree or a fresh clone they are reported, not enforced, so
    that the evidence checks remain checkout-independent.

## Re-verified Q2 evidence

The two Q2 qualification evidence packages were re-verified at acceptance (not
re-executed):

- Session A: the manifest's 48 entries — 47 package content blobs plus the qualification
  report — were re-derived over Git blobs: 46 content blobs plus the report matched their
  recorded SHA-256 and byte counts, and `verify-evidence.py` is skipped as self-referential.
  The report entry matched at commit `96a1a3c` but not after the parallel commit modified
  it (discrepancy D-04 reproduces this as a false failure caused by CRLF normalization,
  not by content change).
- Session B: the four R1 source blobs and five R1 evidence blobs, the Q1 evidence
  integrity, the historical evidence and decision integrity, and the independent Windows and
  WSL2 campaign results were verified. Its manifest carries no hash or byte content
  (discrepancy D-02) and its verifier cannot pass on its own commit (discrepancy D-03).
- Rollback provenance: five Windows campaign results byte-identical to the pre-rollback
  cleanup inventory; logs and TRX regenerated or freshly executed; all WSL2 results fresh.
- Six TRX campaign counters matched the reported totals.
- Nine negative-control transcripts recorded failures with matching TRX files.
- All historical evidence and decision directories were unmodified.
