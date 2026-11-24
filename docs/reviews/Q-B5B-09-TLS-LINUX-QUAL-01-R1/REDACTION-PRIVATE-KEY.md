# Private Key Redaction Record — Q-B5B-09-TLS-LINUX-QUAL-01

Additive record. No historical evidence or report has been rewritten.

## 1. Provenance of the removed material

Object: `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/results/exec-05-44889075318/cert.key`
Blob: `0cb99688051feb93832f12f3d0625591f7121983`

Determined without printing key material:

- Introduced by **exactly one** commit, `d088e2e`
  ("docs(reviews): add Q-B5B-09 TLS Linux EXEC-05 report A"), and never modified.
- Present at HEAD before this remediation and in **no** published ref
  (`origin/main`, `origin/feature/protocol-spec`,
  `origin/qualification/q-b5b-09-tls-linux-qual-01`), confirming no exposure beyond
  this local repository.
- The sibling `cert.pem` is a **self-signed certificate for CN `localhost`** with a
  **24-hour validity window** (2026-11-21 → 2026-11-22 UTC). This is a disposable
  ephemeral test credential, not a service identity.
- `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R1/harness/Program.cs` generates an
  ephemeral certificate *and* key per run via `Certificates.Create(...)` and writes
  them to `<results>/cert/server.{crt,key}`. The committed object is therefore a
  leaked run artifact, not a harness input.
- No manifest, journal or evidence index references the key's contents.
- No published CI artifact contains it (no CI has ever run for this repository).

**Conclusion: the credential is disposable, harness-generated, and used nowhere
outside the qualification harness.** No revocation or rotation of a live service is
required. No actual service or reusable credential is affected.

## 2. History boundary and rewrite

`git merge-base --is-ancestor origin/main d088e2e` → true, so the affected range is
**entirely unpublished** and published history required no rewriting.

Smallest defensible rewrite:

    git filter-branch --force --index-filter \
      'git rm --cached --ignore-unmatch <path>' d088e2e^..HEAD

Applied to **11 of 45** unpublished commits (`d088e2e` and its 10 descendants). The
preceding 34 commits were not rewritten and retain their original identities.

Verification of the sanitized range:

| Check | Result |
|---|---|
| Commit count | 45 → 45 (no squashing) |
| `origin/main` an ancestor of new HEAD | yes |
| Commits ahead of `origin/main` | 45; `origin/main` ahead of HEAD | 0 → normal fast-forward |
| Commits still containing the key | **0** |
| Key resolves in HEAD | no |
| `cert.pem` (public certificate) | **preserved** |
| `git diff 7429c6b..HEAD -- src\|tests` | **EMPTY** |
| Author + subject preserved | **45/45** |
| Live secret scan | no private key material detected |
| R4 / R4-R1 / R4-R2 evidence | `VERIFIER: PASS` each |

**Acknowledgement:** sanitized artifacts are **not** byte-identical to the originals.
The blobs of the 11 rewritten commits differ from their pre-rewrite blobs by the
removal of exactly one path; all other content is preserved. Historical evidence is
otherwise unchanged and was not regenerated.

`refs/original/` was removed after verification so the sanitized history is clean.
The sensitive object remains reachable **only** from these strictly local recovery
refs, which are never to be published:
`consolidation-backup/main-8341d23-pre-sanitize`,
`consolidation-backup/main-9834900`.

## 3. Commit identity map (old → new)

Positions below `d088e2e` are unchanged (identity preserved):

| Old | New | Subject |
|---|---|---|
| d088e2eea | da63a10bf | docs(reviews): add Q-B5B-09 TLS Linux EXEC-05 report A |
| 8ddedea43 | 54afba6bd | docs(reviews): add Q-B5B-09 TLS Linux independent qualification review |
| 2752dbceb | fec091115 | Q-B5B-09-TLS-LINUX-QUAL-01-R1: conformance remediation harness |
| 1cfb2640c | c093404b6 | Q-B5B-09-TLS-LINUX-QUAL-01-R1-Q1: Independent Harness Readiness Review |
| 6948594b6 | 18272b18a | Q-B5B-09-TLS-LINUX-QUAL-01-R2: Targeted TLS Cap Measurement Remediation |
| 36d7b69bd | 7e0765035 | Q-B5B-09-TLS-LINUX-QUAL-01-R3: Actual TLS Ciphertext Measurement Correction |
| df5011ffb | 38edab7 | Q-B5B-09-TLS-LINUX-QUAL-01-R4: integrated harness stabilization |
| 1eb03bdee | 5dcb703 | Q-B5B-09-TLS-LINUX-QUAL-01-R4-R1: targeted review findings remediation |
| 784cc3365 | 8f2a55c | Q-B5B-09-TLS-LINUX-QUAL-01-R4-R2: F6 evidence uniqueness remediation |
| 9834900bc | 70e939b | Q-B5B-09-TLS-LINUX-QUAL-01-R4-R2-Q1: independent F6 remediation qualification |
| 8341d23e9 | (see git log) | Owner acceptance record, consolidation assessment… |

The two R4/R4-R1 milestones above were previously reported as `df5011f…` and
`1eb03bd…`; those identities referred to the pre-rewrite blobs and are the values
recorded in the historical reports above them. They are now `7e0765035`-descended
commits with different blob ids and identical authorship and subjects. Earlier
reports quoting the old ids remain historically accurate descriptions of the
pre-rewrite repository.

## 4. Prevention implemented

1. **`.gitignore`** — narrow rules excluding generated private keys under the
   qualification results tree only (`…/results/**/cert/*.key`). Deliberately not a
   broad `*.key`/`*.pem` pattern, so legitimate source keys and public certificates
   elsewhere remain trackable and visible.
2. **Content-based secret scanner** —
   `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R1/tools/scan_for_private_keys.py`,
   detecting PEM private-key armour **independently of file extension**. Ships with
   12 positive and negative regression controls (5 armour variants, embedded armour;
   and public certificates, public SSH keys, prose that merely mentions the header,
   empty files, and name-only false positives). All pass.
3. **Validation workflow** — a "Reject private key material before executing" step
   was added to `.github/workflows/q-b5b-09-tls-linux-qual-01.yml` immediately after
   checkout, so a commissioned campaign fails fast on key material.
4. **Runtime generation** — already the behaviour of the harness; the retention
   convention is now documented and enforced by the rules above, so retained evidence
   contains public certificates and journals but no private keys.

No production behaviour was modified. No changes outside the qualification
infrastructure were required.