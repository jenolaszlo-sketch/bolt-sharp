# Q-B5B-09-TLS-LINUX-QUAL-01-EXEC-03 — GitHub Actions PR Qualification Execution Report

**Formal disposition: B — supported with qualifications.**

Native-Linux execution was performed on GitHub Actions, but the registered bounded campaign driver is a preparation/platform driver; it did not execute the full campaign. Therefore the formal disposition is **B**, not A.

## 1. Preflight

- Repository: `D:/projects/bolt-sharp`
- Branch: `main`
- Execution report commit: `5f6bf0d0dca47c9cbaeb6c98db09e91f54a1490c`
- Execution report tree: `2e9a7d48c01bdca0f03e7739083717b0516dd70e`
- Parent: `557dc09374bceccb3bf271e051767eb7744a7804`
- Qualified investigation revision: `7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f`
- Qualified investigation tree: `2fdf1c3b96586465eb7547d23b6b1792240692ba`

## 2. Repository/evidence integrity

- E1 evidence verifier: `PASS 36/36`.
- E1 package: `docs/reviews/evidence/Q-B5B-09-TLS-E1`.
- Qualification plan: `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/campaign-plan.json`.
- Campaign driver: `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/run-campaign.py`.
- Workflow: `.github/workflows/q-b5b-09-tls-linux-qual-01.yml`.

## 3. Workflow adaptation

- Trigger adapted to `pull_request`.
- No `workflow_dispatch` retry.
- Runner changed to `ubuntu-24.04`.
- PR target: `main`; PR base: `main`.
- PR #1: `https://github.com/jenolaszlo-sketch/bolt-sharp/pull/1`.
- PR state: open; `mergeStateStatus: CLEAN`; no merge was performed.
- Remote `main` was not updated or merged.

## 4. Native Linux execution evidence

### 4.1 GH Actions run

- Run ID: `44810640498`
- Job ID: `136545986664`
- Runner image: `ubuntu-24.04` (Azure, standard runner)
- Operating system: `Ubuntu 24.04.5 LTS`
- Kernel: `Linux runnervmeozm8 7.0.0-1017-azure #17~24.04.1-Ubuntu SMP PREEMPT Thu Sep 24 18:46:57 UTC 2026 x86_64 x86_64 x86_64 GNU/Linux`
- Executed qualification branch: `qualification/q-b5b-09-tls-linux-qual-01`
- Executed branch tip: `c77a95b6b6022296e30efb2a8ad168c46203f877`
- Executed branch tree: `e8a4ae979c9bd49edaa698326f1033261f2fcf1e`
- Workflow `Check out` step: `c77a95b6b6022296e30efb2a8ad168c46203f877`

### 4.2 Artifacts/receipts

- Artifact ID: `14667170710`
- Artifact zip SHA-256: `ef874e7794a93c5a50c670f8330dcc2661aca19406dd041ceccb899381167f3b`
- Campaign summary: `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01/results/q-b5b-09-tls-linux-qual-01-44810640498/campaign-summary.json`
- Campaign summary SHA-256: `6B240BCCC2A447791360466AF206E0D7FF4752FC451AB294B25C191E7DEDD766`
- Campaign summary size: `5455` bytes
- Campaign summary status: `prepared`
- Campaign summary note: `preparation driver; owner-authorized native-Linux campaign not executed`

### 4.3 Budget compliance

- Job timeout: `45 min`.
- Campaign step timeout: `2700 s`.
- Per-attempt watchdog: `3500 ms`.
- Discovery attempts bound per cell: `4`; cap cells: `3`; maximum runs per cell: `6`.
- Network: loopback only.
- Actual GH Actions job: completed in `8 s`.
- Node 24 deprecation warnings: non-fatal.

## 5. Formal qualification interpretation

**B — supported with qualifications.**

- Native-Linux runner execution is supported: the run executed on GitHub-hosted Ubuntu 24.04 on Azure and completed successfully.
- However, the campaign driver executed by the run is the registered preparation driver. It validates the native-Linux gate and writes `campaign-summary.json`; it does not execute the full registered bounded campaign.
- Therefore native-Linux TLS close_notify qualification is **not supported** as a formal qualification.
- The smallest next commission is the actual native-Linux campaign execution on `ubuntu-24.04`.

## 6. Repository state after report

- Branch: `main`
- Status: clean after execution report commit.
- Commit: `5f6bf0d0dca47c9cbaeb6c98db09e91f54a1490c`
- Tree: `2e9a7d48c01bdca0f03e7739083717b0516dd70e`
- Parent: `557dc09374bceccb3bf271e051767eb7744a7804`
- One worktree, zero stashes.
- Remote qualification branch: `qualification/q-b5b-09-tls-linux-qual-01`
- Remote `main`: unchanged.
- No production, test, API, or dependency changes.

## 7. Recommended smallest next commission

**Q-B5B-09-TLS-LINUX-QUAL-01-EXEC-04 — full native-Linux campaign execution on `ubuntu-24.04`.**

Actions:
1. Execute the registered bounded campaign on GH Actions with the current PR branch.
2. Replace the preparation driver with the actual independent bounded campaign implementation.
3. Capture genuine pending terminal-write and close_notify evidence, interruption results, byte-cap enforcement, cleanup/accounting, peer receipt, negative controls, and budget compliance.
4. Commit execution evidence/report on `main` and open/update the qualification PR.
5. Do not push remote `main` or merge the qualification PR.
