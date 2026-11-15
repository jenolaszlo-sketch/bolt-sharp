# B5-B-TS1-OWNER-01 evidence

[Owner acceptance and closure](../../../decisions/B5-B-TS1-OWNER-01-owner-closure.md)

- [verification.json](verification.json) records the owner audit of the pinned Q1 and
  producer manifests, historical integrity, the six prior decisions, and archived outcomes.
- [manifest.json](manifest.json) inventories the owner record and every file in this
  package except the manifest itself.
- [verify-evidence.py](verify-evidence.py) verifies the owner commit, its Q1 parent,
  the package inventory, and the commit scope using only Git and the Python 3 standard library.

## Hashing semantics

Owner package entries use SHA-256 over the exact contents returned by
git cat-file blob <owner-commit>:<repository-relative-path>. Each entry also records
the byte count. The package uses a local Git attribute to preserve byte-exact checkout
contents. The owner decision hash is computed from the staged Git blob. The owner
commit tree pins the manifest itself.

The recorded historical audit uses raw working-tree SHA-256 for the documented
historical manifests and producer entries, with the five historical B3-B source
entries read as raw Git blobs from their manifest-declared implementation commits.
Git's clean-filter semantics apply only to the recorded comparison of working
copies of prior owner decisions with their committed blob IDs.

## Verification

From the repository root at the owner commit, run:

```text
python docs/reviews/evidence/B5-B-TS1-OWNER-01/verify-evidence.py
```

From a later checkout, pass the owner commit ID as the sole argument. The verifier
reads the manifest and every payload from that commit, verifies the exact Q1
parent and tree, and requires an additions-only commit containing exactly the
owner record and package files.

For the upstream Q1 evidence on Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/reviews/evidence/B5-B-TS1-Q1/Verify-Evidence.ps1 -Commit b196db8f0bc612b7f1ef3f78412480239c3f04db
```

Q1's verifier invokes Git through cmd.exe, which needs git.exe on PATH.
The campaign totals in the owner audit are from committed logs and summaries.
