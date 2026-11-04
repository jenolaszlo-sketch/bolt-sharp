# B2-B-Q1 — Independent qualification handoff

**Status:** OPEN — awaiting an independent reviewer (owner acceptance PENDING)
**Date:** 2026-11-04
**Reviewed task:** B2-B (Bolt handshake), see
[B2-B-bolt-handshake.md](B2-B-bolt-handshake.md)
**Canonical branch:** `main`
**Reviewed entry:** the additive commit that adds `src/BoltSharp/Handshake/`,
`tests/BoltSharp.Tests/Handshake/` and the two B2-B handoff documents. Its exact SHA is recorded
in the implementer's final handoff message; it descends directly from
`a46c916e7f6e72e315504e2d284921d406ee6c74`.

This handoff contains no implementation and makes no compatibility claim. The reviewer — **not
the implementer** — produces the qualification report.

---

## 1. Independence requirement

The qualifier must not have written the B2-B implementation, this handoff, or the golden
fixtures. Independence is asserted per session; a shared repository Git author identity is not
evidence of a distinct reviewer. The reviewer must treat every B2-B file as potentially
incorrect and must attempt to falsify it rather than re-read it.

## 2. Reviewed entry and ancestry

1. Confirm the reviewed entry descends directly from `a46c916e7f6e72e315504e2d284921d406ee6c74`.
2. Confirm the change set is **purely additive**: `src/BoltSharp/Handshake/` and
   `tests/BoltSharp.Tests/Handshake/` are new; no existing source, test, project, props or
   solution file is modified. `git diff --stat a46c916..<entry> -- ':!src/BoltSharp/Handshake'
   ':!tests/BoltSharp.Tests/Handshake' ':!docs/reviews/B2-B*'` must be empty.
3. Record the exact Git object identity (commit and tree) of the reviewed entry.

## 3. Files in scope

- `src/BoltSharp/Handshake/BoltHandshakeException.cs`
- `src/BoltSharp/Handshake/BoltProtocolVersion.cs`
- `src/BoltSharp/Handshake/BoltVersionRange.cs`
- `src/BoltSharp/Handshake/BoltVarInt.cs`
- `src/BoltSharp/Handshake/BoltHandshakeLimits.cs`
- `src/BoltSharp/Handshake/BoltHandshakeOptions.cs`
- `src/BoltSharp/Handshake/BoltHandshakeResult.cs`
- `src/BoltSharp/Handshake/BoltManifestV1.cs`
- `src/BoltSharp/Handshake/BoltHandshakeNegotiator.cs`
- `src/BoltSharp/Handshake/HandshakeByteReader.cs`
- `tests/BoltSharp.Tests/Handshake/*` (fixtures, transport and 155 tests)

## 4. Authoritative sources and exact sections

Retrieve and cite on the review date:

- Bolt handshake specification, **"Bolt identification"** — magic `60 60 B0 17`.
- **"Version negotiation"** — exactly four big-endian 32-bit proposals, version zero as a
  placeholder, first match by client preference, single-version response or zero-then-close, and
  the four worked examples.
- **"VarInt"** — 7-bit little-endian groups, continuation high bit, the `FF 82 71` = 1851775
  worked example, maximum unsigned 64-bit.
- **"Handshake Manifest v1"** — marker `00 00 01 FF`, VarInt `N`, `N` version ranges in Bolt
  4.3 style, VarInt capabilities, client concrete 4.0-style selection and capability subset,
  pipelined first message, and the full worked byte example.
- **"Bolt version 4.3"** — range layout (`00 range minor major`), no spanning across a major.
- **"Bolt version 4.0"** — concrete layout (`00 00 minor major`).

Primary URL: <https://neo4j.com/docs/bolt/current/bolt/handshake/>. The upstream AsciiDoc at
`neo4j-drivers/docs-bolt` `modules/ROOT/pages/bolt/handshake.adoc` is the same authority and is
acceptable for the full worked example.

## 5. Falsification targets

At minimum, independently attempt to falsify each of the following (fresh fixtures or literal
hex, not only the production encoders):

1. **Magic and preamble.** Wrong magic, a 1–3 byte prefix, a 17–19 byte preamble, and a
   preamble followed by extra bytes.
2. **Proposal framing.** Fewer or more than four proposals; the committed reader must read
   exactly 16 bytes.
3. **Version encoding.** `00 00 01 04` = 4.1, `00 00 08 05` = 5.8, `00 00 00 06` = 6.0,
   `00 00 FF FF` = 255.255; a set reserved byte (`01 xx xx xx`) must fail; a range that would
   cross a major (`00 02 01 04`) must fail.
4. **Preference ordering.** Multiple matching proposals select the first; a range proposal
   selects the highest supported minor inside it; placeholders are skipped.
5. **Zero-version rejection.** No match writes `00 00 00 00` and requests closure, and writes no
   other byte.
6. **Manifest offer generation.** For a chosen supported set, the emitted ranges, order and
   capability VarInt match an independent encoder; check the specification example exactly.
7. **Manifest selection.** An offered concrete version and a capability subset are accepted; an
   unoffered version, a capability superset, and a range-encoded selection are rejected without a
   published result.
8. **VarInt.** `01`, `7F`, `80 01`, `FF 82 71`, ten-byte 2^64 − 1, tenth-byte overflow,
   eleven-byte continuation, mid-VarInt truncation (sync and async), and a VarInt split across
   one-byte reads.
9. **Resource bounds.** A declared range count above `MaxManifestVersions` must fail **before**
   allocating; a handshake beyond `MaxHandshakeBytes` must fail; no unbounded allocation from an
   attacker-controlled length.
10. **Pipelining.** After a legacy or manifest handshake, a pipelined first message must remain
    readable and decode through the B2-A `BoltMessageReader`; verify the exact number of bytes
    consumed by the handshake.
11. **Sync/async equivalence.** The same input yields the same result fields and the same written
    bytes on both paths.
12. **Ownership.** The negotiator must not dispose the supplied stream and must not write a
    response on a malformed magic.

## 6. Reproduction and regression gate

Environment: Windows `10.0.26200`, `win-x64`, .NET SDK `10.0.401`, target `net8.0`, .NET 8
runtime `8.0.31`, MSBuild `18.9.11`. From the canonical root:

```powershell
dotnet build BoltSharp.slnx -c Debug
dotnet test  BoltSharp.slnx --no-build -c Debug
dotnet test  BoltSharp.slnx --no-build -c Debug --filter "FullyQualifiedName~Tests.Handshake"
dotnet test  docs/reviews/probes/B2-A-Q2/B2AQ2.Probes.csproj -c Debug
```

The implementer reports **759** solution tests (604 pre-existing + 155 new handshake) and
**18** separate Q2 probes, all passing, with 0 warnings and 0 errors. The reviewer must confirm:

- the **604-test pre-existing solution baseline is unchanged** (509 B1 + 54 B2-A producer +
  41 B2-A-Q1);
- the new handshake tests are **155** and all pass;
- the separate Q2 probes remain **18/18**;
- no new dependency was added to the core or test project.

Do not state a passing count that was not produced by an executed run.

## 7. Required reviewer deliverable

A report that:

- states the verdict and the exact reviewed commit/tree;
- binds every claim to a command, output and environment;
- lists concrete findings by severity with file/line and minimal reproduction (state clearly if
  none are reproduced);
- distinguishes accepted limitations from defects;
- does not edit the historical B2-A or B2-B records, and does not mark owner acceptance.

Independent reviewer probes may be committed under `docs/reviews/probes/B2-B-Q1/` with captured
evidence under `docs/reviews/evidence/B2-B-Q1/`, excluded from the solution, consistent with the
B2-A-Q2 arrangement. Reviewer probes must not be added to the solution.

## 8. Admissibility and non-claims

- Passing happy-path tests is necessary but not sufficient; the reviewer must falsify boundaries,
  malformed input and resource limits.
- No Neo4j, Neo4j Browser or `Neo4j.Driver` compatibility may be claimed at B2-B. It requires an
  executed integration test at a later executable milestone.
- The B0-A protocol revision remains **PROVISIONAL**; advertising a version in a test
  configuration is not an operational-support claim.
- No NuGet package or published artifact may be produced.

## 9. Entry-authorization summary

- **Implementer:** not the reviewer.
- **Reviewed entry:** the additive B2-B commit descending from `a46c916`.
- **Regression gate:** 604 pre-existing solution tests unchanged; 155 new handshake tests; 18
  separate Q2 probes; 0 warnings, 0 errors.
- **Stop condition:** stop at the B2-B-Q1 gate. Do not begin B2-C and do not record owner
  acceptance.

## 10. Handoff receipt

```text
Task: B2-B-Q1 (handoff)
Implementation record: docs/reviews/B2-B-bolt-handshake.md
Baseline commit: a46c916e7f6e72e315504e2d284921d406ee6c74
Reviewed entry: recorded in the implementer's final handoff message
Scope: src/BoltSharp/Handshake/* and tests/BoltSharp.Tests/Handshake/*
Required reviewer: independent of the implementer
Regression gate: 604 pre-existing + 155 new = 759 solution; 18 separate Q2 probes; 0 warnings/errors
Non-claims: no browser/driver compatibility; B0-A revision PROVISIONAL; no NuGet publication
Stop: B2-B-Q1 gate only
```
