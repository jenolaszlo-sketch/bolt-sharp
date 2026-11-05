"""Seal the additive reviewer report/evidence after execution; never rewrites historical paths."""
import importlib.util, json, pathlib, subprocess, sys, time

ROOT = pathlib.Path.cwd()
P = ROOT/'docs/reviews/probes/B0-A-TRACE-Q2'
E = ROOT/'docs/reviews/evidence/B0-A-TRACE-Q2'
REPORT = ROOT/'docs/reviews/B0-A-TRACE-Q2-independent-requalification.md'
spec = importlib.util.spec_from_file_location('runner', P/'Run-Qualification.py')
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
r.integrity()  # reread all protected evidence and pinned identities after all testing
assert not r.git('diff', '--name-only')
assert not r.git('diff', '--cached', '--name-only')
status = r.git('status', '--porcelain=v1', '--untracked-files=all').decode()
for line in status.splitlines():
    assert line[:3] == '?? ', line
    assert line[3:].replace('\\','/').startswith(('docs/reviews/probes/B0-A-TRACE-Q2/',
        'docs/reviews/evidence/B0-A-TRACE-Q2/', 'docs/reviews/B0-A-TRACE-Q2-independent-requalification.md')), line
commands = json.loads((E/'commands.json').read_bytes())
final = next(c for c in commands if c['name'] == 'q2-verified')
counts = final['trx']['counts']
assert counts == {'Passed':20, 'Failed':6} or counts == {'Failed':6,'Passed':20}, counts
assert final['exit_code'] == 1
rebuild = next(c for c in commands if c['name'] == 'q2-verified-rebuild')
assert rebuild['exit_code'] == 0 and not rebuild['warning_lines']
text = REPORT.read_text(encoding='utf-8')
text = text.replace('@@Q2_COUNTS@@',f"{counts['Passed']} passed / {counts['Failed']} failed (exit 1)")
text = text.replace('@@Q2_PASSED@@',str(counts['Passed'])).replace('@@Q2_FAILED@@',str(counts['Failed']))
assert '@@' not in text
REPORT.write_bytes(text.encode('utf-8'))
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
import xml.etree.ElementTree as ET
failed=[]
for name in ['q2-verified','q2-hook-final','q2-prefix-final']:
    root=ET.parse(E/'trx'/f'{name}.trx').getroot()
    for result in root.findall('.//t:UnitTestResult',ns):
        if result.attrib['outcome']=='Failed':
            failed.append({'suite':name,'name':result.attrib['testName'],
                'message':result.findtext('.//t:Message',namespaces=ns),
                'stdout':result.findtext('.//t:StdOut',namespaces=ns)})
r.save('decisive-failures.json', failed)
source=(P/'LifecycleProbes.cs').read_bytes()
r.save('reviewer-run-provenance.json', {
    'session': '01a1acb3-b403-7520-b3ca-fd3c38d39e40',
    'author_independence':'UNVERIFIED; distinct current Codex session; no producer implementation participation in this session',
    'model_provenance':'runtime describes GPT-6-based Codex; external identity attestation unavailable',
    'decisive_source_sha256':r.digest(source),
    'decisive_assembly_sha256':r.digest((P/'bin/Debug/net8.0/B0ATRACEQ2.Probes.dll').read_bytes()),
    'build_command':rebuild['argv'],'test_command':final['argv'],
    'initial_runs':'q2 and q2-corrected superseded for native lifecycle qualification; Q05 timeouts are reviewer adapter issues. Timestamp-preserving copies allowed stale incremental compilation. q2-final corroborates 20/6; decisive forced rebuild removes ambiguity.',
    'synchronization':'explicit read-entry/release tasks; actual wire responses; timeouts detect deadlock only',
    'resource_claim':'native accept/handler task completion and client-list drain, bounded port-refusal probes; no exhaustive leak/race claim',
    'evidence_scope':'new Q2 paths only; historical sources/evidence unchanged',
})
r.save('ownership-and-final-precommit.json', {
    'qualified_head':r.HEAD,'qualified_tree':r.TREE,'branch':'main','worktree_count':1,
    'prewrite_clean_index_and_working_tree':True,
    'competing_boltsharp_runner_or_writer_observed':False,
    'retained_build_nodes':'ten unchanged MSBuild nodes with absent parent, CPU counters unchanged between prewrite snapshots',
    'other_agent_services':'OpenCode desktop/service present; no competing BoltSharp writer demonstrated',
    'authorization':'one modifying agent: current owner-authorized Q2 session; no delegation or subagents',
    'limitation':'observational exclusivity; no OS-enforced writer lock',
    'precommit_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),
    'precommit_status':status.splitlines(),
})
subprocess.run(['python',str(P/'Audit-Results.py')],cwd=ROOT,check=True,stdout=subprocess.DEVNULL)
def entry(path, base):
    data=path.read_bytes()
    return {'path':str(path.relative_to(base)).replace('\\','/'),'sha256':r.digest(data),'bytes':len(data)}
sources=[REPORT]+[path for path in P.rglob('*') if path.is_file() and not any(
    part in ('bin','obj','__pycache__') for part in path.relative_to(P).parts)]
r.save('reviewer-artifacts.json', {'algorithm':'SHA256','description':'report and reviewer-owned source artifacts; paths relative to repository',
    'files':[entry(path,ROOT) for path in sorted(sources)]})
files=[path for path in E.rglob('*') if path.is_file() and path.name!='sha256.json']
r.save('sha256.json', {'description':'B0-A-TRACE-Q2 independent reviewer evidence manifest',
    'algorithm':'SHA256','qualified_head':r.HEAD,'qualified_tree':r.TREE,
    'files':[entry(path,E) for path in sorted(files)]})
for manifest,base in [(E/'sha256.json',E),(E/'reviewer-artifacts.json',ROOT)]:
    data=json.loads(manifest.read_bytes())
    for item in data['files']:
        file=base/item['path']; assert entry(file,base)==item
    print(manifest.name,len(data['files']),'verified',flush=True)
print('Report disposition: REMEDIATION REQUIRED; F01 CLOSED; exact-source probes 20 pass / 6 fail',flush=True)
