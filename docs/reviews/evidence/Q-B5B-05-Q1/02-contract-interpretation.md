# Independent Contract Interpretation — Q-B5B-05-Q1

Reviewer session for Q-B5B-05-Q1 (author/producer separation recorded in the qualification report).
This interpretation is derived from the binding contracts themselves. Producer evidence is used as
input, never as authority.

## 1. The original requirement, verbatim

`Q-B5B-05` is defined in **B5-B-OWNER-01-owner-acceptance.md §23.2** (new B5-B binding
qualifications):

> **Q-B5B-05 — Same-connection recovery after IGNORED not established.** After the observed
> FAILURE/IGNORED sequence the driver destroys the connection; RESET recovery of that connection
> was not demonstrated.

The identical wording is carried by the commissioning record of the producer
(`docs/reviews/evidence/Q-B5B-05-PROBE-01/commissioning-record.md` §1).

## 2. Binding context, as quoted from the contracts

| Source | Text |
|---|---|
| B5-B-OWNER-01 §20 | "RETAIN a separate qualification for same-connection RESET recovery after the observed IGNORED scenario (new `Q-B5B-05`): it was **not established** — under the accepted classification the driver destroys the connection instead of sending RESET. Same-connection recovery is **not** covered by this narrowing, and no related recovery behavior is closed by implication." |
| B5-B-OWNER-01 §19 (F02) | "`50N42` is the bounded classification; drivers may treat refusals as unrecoverable per-connection; refinement is optional and separately authorized." |
| B5-B-OWNER-01 §19 owner disposition | "…more precise classification might improve driver exception categorization, same-connection recovery, pooling efficiency and diagnostics, but it is **not required** for the current bounded acceptance. `50N42` must not be changed without a separate authorized contract assessment…" |
| B5-B-OWNER-01 §25 (non-claims) | "same-connection RESET recovery after the observed IGNORED scenario" is explicitly **not** claimed. |
| B5-B-OWNER-01 §29.9 | "Same-connection RESET recovery after the observed IGNORED remains **unverified** (`Q-B5B-05`)." |
| B5-B-TS1-OWNER-01 | "`Q-B5B-05` — Same-connection RESET recovery after IGNORED \| **Remains OPEN** as an independent qualification." |
| B5-B-F01-R1-OWNER-01 §18 | "**Formal disposition: `Q-B5B-05` — RETAINED.** Same-connection RESET recovery under the current generic classification remains separately qualified; **recovery on a new physical connection is not recovery of the original connection.**" |
| Q-B5B-09-PLAIN-OWNER-01 §11 | recommends a bounded "`Q-B5B-05` — same-connection RESET-recovery demonstration … not a production change" |

## 3. What the requirement asserts — and what it mandates

The requirement combines a **fact pattern** and a **gap statement**:

1. Fact pattern (accepted record): after the observed FAILURE/IGNORED sequence
   (`50N42` FAILURE for RUN + zero-field `B0 7E` IGNORED for pipelined PULL) the official driver
   destroys the physical connection.
2. Gap statement: "RESET recovery of that connection was not demonstrated" — passive voice; the
   performing agent is **not named**.

The text is therefore a qualification gap — *whether the affected connection can be RESET-recovered
was never established* — not a positive mandate on any party. Three readings were tested against
the contract corpus:

### 3.1 Reading S (server capability) — sustained

"RESET recovery of that connection" is agent-neutral. Discharging the gap requires demonstrating
that after a recoverable FAILURE the connection can be RESET-recovered (subsequent successful
operation on the same physical connection). Nothing in the text restricts the RESET initiator.
The retained qualification would be satisfied by demonstrating the capability on any client that
correctly speaks the wire protocol, with the driver's destructive behavior recorded separately.

### 3.2 Reading D (driver-initiated RESET) — not mandated

The text does **not** say "the driver must send RESET". It records the opposite side of the
observation as *accepted* behavior: §19 explicitly entitles drivers to treat refusals as
unrecoverable per-connection, and §20 states the driver's destruction of the connection as part of
the accepted classification regime. F02 refinement (which would change driver classification) is
"not required for the current bounded acceptance". No owner decision mandates driver-initiated
RESET after FAILURE; the closest owners' text (F01-R1: "recovery on a new physical connection is
not recovery of the original connection") defines *what does not count*, not *who must act*.

### 3.3 Reading X (interoperability purpose) — recorded, not adopted

Because B5-B is an official-driver interoperability milestone, an owner *could* intend
Q-B5B-05 to cover the driver's recovery path specifically. The text does not compel that reading,
and §19/§25 point the other way, but this is precisely the fork the owner must resolve — it is
**Options A vs B** in the owner recommendation, not a reviewer call.

### 3.4 Answer to commission question C

The original Q-B5B-05 acceptance contract requires:

1. **Server capability for same-connection RESET recovery after the recoverable FAILURE in the
   observed sequence — YES.** The gap "RESET recovery of that connection was not demonstrated" is
   discharged only by demonstrating that capability.
2. **Driver-initiated same-connection RESET after FAILURE — NOT REQUIRED by the original text.**
   The driver-destroys-the-connection behavior is part of the accepted record (§19, §20, §25);
   demonstrating it through the official driver is not stated as a condition of closure, and a
   documented official-driver limitation may be carried by the owner decision.
3. **Both — NO** as a joint mandate.

Two consequences follow, and both are honored in this qualification:

- No silent weakening: this commission does **not** treat fresh-connection recovery as
  same-connection recovery, and does not relax any accepted oracle (B5-B-TS1 remains CLOSED and
  untouched).
- No silent broadening: this commission does **not** claim official-driver post-failure RESET
  interoperability, because the raw-protocol demonstration never crossed the official driver and
  the official driver reproducibly refuses to RESET after the failure.

## 4. Sequence and state-transition requirements derived from the contracts

From the same corpus plus the production state-machine contract
(`src/BoltSharp/Sessions/BoltConnectionSession.cs`, reviewed during this commission):

| Requirement | Source |
|---|---|
| A recoverable FAILURE in this engine is the unsupported-query refusal: `50N42 Neo.ClientError.Request.Invalid`, session → `Failed`, connection stays open | Q-B3C-02 / B5-B §19 / session `UnsupportedQueryFailure()` |
| Work messages in `Failed`/`Interrupted` answer zero-field `IGNORED` (byte-exact `B0 7E`) | B3-A / B5-B §20 |
| `RESET` is admissible from `Ready`, `Streaming`, `Failed`, `Interrupted` and returns to `Ready` with empty `SUCCESS`; outside those it is a terminal protocol violation | session `HandleReset()` |
| A provider `Dispose` failure during RESET retirement is irreversible (terminal `50N00`, Defunct) | B3-B-R2 / B5-B §21 |
| A Defunct session processes nothing; `RESET` after a terminal failure produces no response | B3-B / session `ReceiveBytes` |
| Physical connection identity is proved by accepted-socket facts, not by driver/pool reuse | B5-B §15/§20 / F01-R1 closure sentence |
| Terminal (terminal-class) failures are inadmissible as "recoverable" for this qualification | B3-B §9 / commission §6 |