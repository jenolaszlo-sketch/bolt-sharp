# B0-A-TRACE-R3 evidence addendum

This addendum records two evidence limitations the Q3 reviewer found in the historical
B0-A-TRACE-R2 evidence and how the new R3 evidence avoids them. The historical R2 evidence is
**not** rewritten or repaired.

## Earlier R2 limitations

1. **Omitted manifest entry.** The R2 `sha256.json` manifest listed 30 evidence files plus its
   report, but it omitted `authentic-handshake-hashes.json`, so one committed evidence blob was
   not covered by the manifest.
2. **Non-reproducible relative build command.** The R2 GetStream-hook build command logged a
   repository-root-relative `ListenerSource` value. MSBuild resolves that property relative to
   the probe project, so the literal command failed from the repository root with `CS2001`. A
   later absolute-path rebuild succeeded; the relative command is retained as a failed run in
   the Q3 review.

Neither limitation invalidates the R2 results, and neither is changed here.

## R3 evidence treatment

1. **Complete manifest.** `docs/reviews/evidence/B0-A-TRACE-R3/manifest.json` lists **every**
   file committed under `docs/reviews/evidence/B0-A-TRACE-R3/` except the manifest itself,
   including `authentic-handshake-hashes.json`, the pre-R3 control snapshot, the R3 GetStream
   hook snapshot, every command log and every TRX. The count of listed files equals the count of
   committed evidence files minus one (the manifest).
2. **Hashes verified against committed bytes.** Because `.gitattributes` sets `* -text` in the
   evidence directory, committed blob bytes equal working-tree bytes; the manifest SHA-256 values
   were recomputed from the committed blobs after the evidence commit and match.
3. **Rooted commands.** All R3 commands in `commands.json` use absolute `D:\projects\bolt-sharp\...`
   paths for `ListenerSource`, `R3TestSource`, results directories and the working directory, so
   they reproduce from the repository root. Each entry records the working directory and the exit
   code.
4. **Pre-remediation negative control.** The before-fix negative control links the byte-faithful
   pre-R3 listener snapshot at
   `docs/reviews/evidence/B0-A-TRACE-R3/controls/pre-r3-listener.cs` (Git blob
   `24d530498aabaa0ec5d1275ad040a6e984afbe43`, identical to the entry `HEAD` blob) and runs the
   R3 regression source against it.
