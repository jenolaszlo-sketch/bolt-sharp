# Q-B5B-09-OWNER-01 evidence

[Owner decision](../../../decisions/Q-B5B-09-OWNER-01-production-remediation-scope-decision.md) ·
[Plaintext implementation specification](../../../decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md)

- [verification.json](verification.json) records exact entry ancestry, manifest and prior
  decision identities, TLS journal results, patch replay and independently reconciled
  archived Windows test counts.
- [audit-reviewed-inputs.py](audit-reviewed-inputs.py) recomputes those results using
  the pinned commits. Patch replay is in a temporary directory and executes no
  prototype, test, or network campaign.
- [manifest.json](manifest.json) covers the two decision documents and all package
  files except itself.

From the repository root at the owner commit, or with its hash as an optional
argument from a later checkout, run:

```text
python docs/reviews/evidence/Q-B5B-09-OWNER-01/verify-evidence.py
```

The verifier uses Python 3 and Git. It checks the sole parent and tree, complete
inventory, additions-only scope, byte counts and SHA-256 over exact Git blob
payloads. It then runs the committed audit and compares its output with the
recorded results. The Git commit tree pins the manifest itself.
