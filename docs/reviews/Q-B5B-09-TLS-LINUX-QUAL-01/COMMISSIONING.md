# Commissioning — Q-B5B-09-TLS-LINUX-QUAL-01 (bounded native-Linux TLS graceful-close qualification)

**Campaign status: B — not claimable yet; preparation complete.**

This campaign is the native-Linux sibling of Q-B5B-09-TLS-E1
(`docs/reviews/Q-B5B-09-TLS-E1`): a bounded, observational, repeatable
qualification campaign for native-Linux TLS graceful close.

## Why B is not claimable yet

- The current terminal is Windows 11 and no accepted native Linux execution is
  available here; WSL2 is not accepted as native Linux (it is not).
- The campaign is prepared to run on GitHub Actions (`ubuntu-latest`), the only
  native-Linux execution environment evaluated from public documentation for this
  environment (see the preparation report, §3.2). No native-Linux execution has
  been performed.
- The retained E1 evidence is Windows + WSL2; it supports no native-Linux
  close_notify or verified-peer-receipt claim.

Therefore the campaign cannot claim native-Linux qualification from E1 or from
the local Windows terminal. A native-Linux qualification claim requires both:

1. an actual native-Linux execution of the campaign (ubuntu-latest on GitHub
   Actions), and
2. owner authorization for the remote execution (below).

## Owner authorization for remote execution

The workflow is intentionally not self-triggering (`workflow_dispatch` only).
Remote execution is a new execution of an existing qualification campaign and
requires:

- repository/organization-owner authorization before dispatching
  `q-b5b-09-tls-linux-qual-01.yml`;
- review of the pinned revision (`7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`),
  the bounded budgets, and the negative controls before enabling;
- the workflow runs with `permissions: contents: read` — it cannot push, deploy,
  or modify the repository;
- results are published as workflow artifacts and job summaries only; nothing is
  pushed.

## Stop condition (no native environment)

If no native-Linux environment is available or authorized, the campaign stops at
the native-Linux gate and records a stop:

- the workflow's "Verify native Linux runner" step asserts `RUNNER_OS == Linux`
  and `uname -s == Linux` before the campaign runs;
- any non-Linux environment (Windows, macOS, container, or a WSL2-backed runner)
  fails the job with an explicit stop message;
- the runner records the blocked status when the driver stops for the same reason;
- no native-Linux qualification is claimed from a non-native environment.

`ubuntu-latest` is documented as a native Linux VM (public docs:
`docs.github.com/en/actions/reference/runners/github-hosted-runners` — "With the
exception of single-CPU runners, each GitHub-hosted runner is a new virtual
machine (VM) hosted by GitHub"; the single-CPU label `ubuntu-slim` is an
explicitly-documented container on a shared VM and is not used as a standard
native runner).

## What a claimed B means (execution on ubuntu-latest)

A qualified B would mean the bounded campaign on native Linux established:

- induced terminal-write pendency + interruption (L1–L2);
- induced close_notify pendency + interruption via a zero-window peer and/or a
  gated TLS proxy (L3–L4);
- encrypted byte-cap and plaintext byte-cap enforcement (L5–L6);
- independently observed, verified peer application receipt (L7);
- detected negative controls (NC-1, NC-2);
- per-cell and total budgets held; cleanup/accounting balanced.

A claimed B would not change: the retained E1 Windows/WSL2 evidence (non-native),
the formal disposition of Q-B5B-09 (B retained pending native-Linux,
production-capacity and verified-receipt qualifications), and no production
implementation.

## Status trail

- **Q-B5B-09-TLS-E1:** executed on Windows (main terminal) and adapted WSL2;
  disposition B retained: native Linux, production capacity, verified peer
  receipt, and close_notify pendency on native Linux remain outstanding.
- **Q-B5B-09-TLS-DESIGN-01:** design review; the native-Linux close_notify
  qualification strategy is documented in the design review (not implemented).
- **Q-B5B-09-TLS-LINUX-QUAL-01:** campaign prepared (this document); B — not
  claimable yet.
- **Pending:** native-Linux execution on ubuntu-latest with owner authorization.
