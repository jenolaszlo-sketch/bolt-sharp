# Owner Decision Preparation — Q-B5B-05-Q1

Evidence-backed recommendation per commission §17. **This commission issues no owner decision.**

## 1. What is settled (independently, both platforms)

1. **Server-side capability (F01/F02).** After the observed recoverable FAILURE/IGNORED sequence
   (`50N42` + byte-exact `B0 7E`) the connection **can** be RESET-recovered on the **same physical
   connection**: `RESET B0 0F → SUCCESS B1 70 A0`, session returns to `Ready`, a subsequent
   operation succeeds, the connection completes orderly. Proven with byte-exact markers, six
   identity lines, 1/3/10-cycle repetition, TLS, concurrency, and four false-positive controls
   rejected. The original "RESET recovery of that connection was not demonstrated" gap is closed
   for the server capability.
2. **Official-driver limitation (F03).** The pinned Neo4j.Driver 6.4.0 does **not** RESET a
   recoverable-failed connection in the auto-commit path: it destroys the connection and recovers
   on a new physical connection with a new handshake and a new authentication. The driver **does**
   use same-connection RESET in its pooled protocol (D-04), so the capability exists but is not
   invoked after a failure — even on a retry of the same session (D-05), and even with a retry
   time configured against a `50N00` failure (D-03).
3. **Error classification (F04).** The server's failure metadata is protocol-correct per the
   accepted `Q-B3C-02` classification; the driver's behavior follows from its non-retriable
   treatment of that class, which B5-B §19/F02 already accepted. No BoltSharp change is indicated
   by the driver's reconnection.

## 2. Contract permission (see `02-contract-interpretation.md`)

The original Q-B5B-05 text requires demonstrating same-connection RESET recovery; it does **not**
mandate driver-initiated RESET after FAILURE (§19 explicitly entitles drivers to discard such
connections; §25 lists it as not claimed; no owner decision mandates it). Closing on the
demonstrated server capability with a documented driver limitation is therefore **consistent with
the binding text**, provided no official-driver post-failure RESET interoperability is claimed.

## 3. The two primary alternatives

### Option A — Accept the demonstrated server-side capability; close Q-B5B-05 with a documented official-driver limitation **RECOMMENDED**

- **Accept:** F01/F02/F05 — server-side same-connection RESET recovery after the recoverable
  FAILURE/IGNORED sequence, on one physical connection, plaintext and TLS, Windows and WSL2, with
  four negative controls rejected and 728 independently-reproduced assertions (364 × 2 platforms).
- **Carry as documented limitation:** F03 — the official driver's post-failure connection
  destruction and new-connection recovery, under the accepted 50N42 classification (F02/Q-B5B-04
  unchanged); Q-B5B-09 harness availability unaffected.
- **Why permitted:** contract interpretation §3 — the requirement's success condition is the
  demonstrated recovery capability, agent-neutral; the driver behavior is accepted record
  (§19/§20/§25), and this option introduces no claim beyond the evidence.
- **What it does NOT claim:** official-driver post-failure same-connection RESET interoperability
  (never demonstrated); `50N42` change (not authorized, not needed); native-Linux or
  production-capacity coverage.
- **Residual conditions if accepted:** record the limitation as a standing qualification note;
  keep Q-B5B-04 (classification) and Q-B5B-09 (release hardening) open as already owned.

### Option B — Keep Q-B5B-05 open pending a separately authorized Q-B5B-04 investigation

- **Rationale:** if the owner reads Q-B5B-05's purpose as covering the *driver* recovery path
  (the interoperability reading recorded in interpretation §3.3), the qualification stays open
  until a separately authorized Q-B5B-04 investigation determines the error-classification and
  driver-interaction question.
- **What that investigation would need to establish:** driver retry/pool policy mechanisms as
  *observations*, candidate classifications, and whether any protocol-correct classification makes
  the official driver RESET the same connection — this commission found **no evidence** that it
  would, and recorded none as inference.
- **Cost note:** no production change is authorized or implied; Option B defers closure without
  any demonstrated BoltSharp defect to remediate.

## 4. Reviewer statement

Options A and B both preserve the contract; Option A is the smaller, evidence-complete closure
and is consistent with the exact original acceptance requirement. The reviewer recommends
**Option A** and records Option B as the owner's alternative. The selection is the owner's.