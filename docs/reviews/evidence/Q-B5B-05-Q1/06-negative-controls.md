# Independent Negative Controls — Q-B5B-05-Q1

Commission §11: repeat or independently challenge all four producer controls. Each control below
was executed by the **reviewer-owned harness** (its own client, own provider-fault injection, own
oracle) — not by re-stating the producer run — and the fault path, actual protocol observation and
detecting oracle were recorded. Mutations live only in probe-owned harness code inside the
reviewer test process; no canonical production or existing test file is touched. Per-platform
evidence: `windows/nc0*.txt`, `wsl2/nc0*.txt`.

## NC-01 — replacement connection

- **Intended defect:** present a subsequent successful operation performed on a *different*
  physical connection as same-connection recovery.
- **Injection:** reviewer client performs failure + RESET on socket #1, closes it, then performs
  the subsequent operation on a new client that re-handshakes and re-authenticates.
- **Observed path:** `accepted=2`; ids `[conn-0001, conn-0002]`; two provider instances
  (`prepare=2`); two distinct client local ports (e.g. `2964` vs `2965`).
- **Detecting oracle:** identity lines 1/3/4 (accepted count, port change, provider instances).
- **Result:** **Detected** — the oracle's verdict is `SAME_PHYSICAL_CONNECTION=False`; a
  replacement connection cannot satisfy the same-connection oracle.

## NC-02 — missing RESET

- **Intended defect:** report valid recovery although the RESET was never sent.
- **Injection:** after the recoverable FAILURE the subsequent RUN is attempted directly; the RESET
  is sent only after the control observes the IGNORED behavior.
- **Observed path:** the RUN is answered with the byte-exact zero-field `IGNORED B0 7E`; the
  session stays `Failed` (server trace: a second `sig=0x10 responses=1 state=Failed` after the
  failure); the provider prepare count stays **1** through the ignored RUN (the provider is not
  consulted while failed — assertion-checked); only the RESET (`B1 70 A0`) makes the subsequent
  RUN succeed (evidence file then shows `provider prepares: 2`).
- **Detecting oracle:** expected `SUCCESS` response mismatch + provider-not-consulted counter.
- **Result:** **Detected** — subsequent work remains IGNORED until valid recovery occurs; no
  valid recovery can be claimed without the RESET.

## NC-03 — recovery-response fraud (disposal failure during RESET retirement)

- **Intended defect:** present a RESET that does not answer the successful recovery response as
  valid recovery.
- **Injection:** reviewer-owned provider whose operation `Dispose` throws exactly once, armed
  after an outstanding result exists, then RESET is sent so the session must retire the result.
- **Observed path:** `RESET → FAILURE` with `gql_status 50N00` /
  `neo4j_code Neo.DatabaseError.General.UnknownError` (byte-exact FAILURE recorded); server trace
  `sig=0x0F responses=1 state=Defunct`; `DisposeAttempts=1`; no subsequent response arrives; the
  connection completes closed.
- **Detecting oracle:** the RESET response is `B1 7F …` and never `B170A0`; disposal-failure
  counter; post-terminal silence.
- **Result:** **Detected** — irreversible failure cannot be mistaken for successful RESET
  recovery (confirms the B3-B-R2 fail-closed retirement contract under this demonstration).

## NC-04 — terminal failure substitution

- **Intended defect:** present a terminal Defunct failure as a recoverable one so the recovery
  proof could be satisfied.
- **Injection:** the reviewer sends `BEGIN B1 11 A0` — a valid Bolt 5.8 signature this bounded
  server refuses terminally (`08N06`) with immediate closure.
- **Observed path:** `BEGIN → FAILURE(08N06)`; server trace `sig=0x11 responses=1 state=Defunct`;
  a RESET written to the closing socket receives **no response at all**; the provider is never
  consulted (`prepare=0`); server-side RESET count 0.
- **Detecting oracle:** the expected RESET/`B1 70 A0` recovery sequence cannot exist; a Defunct
  session processes nothing.
- **Result:** **Detected** — the inadmissible failure classification cannot satisfy the
  recoverable-failure oracle.

## Control-integrity statement

For each control the intended defect is distinct, the fault path is reached, the observation is
captured at the wire/server level, and the detecting oracle is the same-connection oracle (or its
components). No unrelated fixture failure is counted as detection: NC-01/02/04 need no exception
at all — their detection is a verdict/counters assertion; NC-03's detection additionally required
the fault to fire exactly once (`DisposeAttempts=1`), which the harness asserts.