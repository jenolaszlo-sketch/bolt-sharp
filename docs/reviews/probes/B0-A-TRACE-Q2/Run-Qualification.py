"""Q2 reviewer runner. Run from the canonical repository root; all durable output is Q2-only.
No checkout, branch, worktree, production edit, or historical evidence write is performed.
"""
import hashlib, json, os, pathlib, platform, subprocess, sys, time, xml.etree.ElementTree as ET

ROOT = pathlib.Path.cwd()
PROBES = ROOT / 'docs/reviews/probes/B0-A-TRACE-Q2'
EVIDENCE = ROOT / 'docs/reviews/evidence/B0-A-TRACE-Q2'
BASE = 'f24afb79ddf9737583e17887e2874c0e781ab354'
HEAD = '3408e6b05af6c449e93979a0ff993ef7e52d9d3b'
TREE = '38ea330bec12666505f978310f39348d620428a3'
SOURCE = 'tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/LoopbackHandshakeListener.cs'
Q2 = str(PROBES / 'B0ATRACEQ2.Probes.csproj')
DRIVER = 'tests/BoltSharp.DriverIntegrationTests/BoltSharp.DriverIntegrationTests.csproj'
COMMANDS = []

def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)

def save(name, data):
    path = EVIDENCE / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')

def digest(data): return hashlib.sha256(data).hexdigest().upper()

def integrity():
    assert git('branch', '--show-current').decode().strip() == 'main'
    assert git('rev-parse', 'HEAD').decode().strip() == HEAD
    assert git('rev-parse', 'HEAD^{tree}').decode().strip() == TREE
    assert git('worktree', 'list', '--porcelain').decode().count('worktree ') == 1
    assert git('rev-parse', '262dc01^').decode().strip() == BASE
    assert git('rev-parse', 'HEAD^').decode().strip() == git('rev-parse', '262dc01').decode().strip()
    changed = git('diff', '--name-status', BASE, HEAD).decode()
    expected_modified = {SOURCE}
    for line in changed.splitlines():
        mode, path = line.split('\t')
        assert (mode == 'M' and path in expected_modified) or (mode == 'A' and (
            path == 'tests/BoltSharp.DriverIntegrationTests/HandshakeTrace/ListenerShutdownRegressionTests.cs'
            or path == 'docs/reviews/B0-A-TRACE-R1-listener-shutdown-remediation.md'
            or path.startswith('docs/reviews/evidence/B0-A-TRACE-R1/'))), line
    protected = ['src/BoltSharp', 'BoltSharp.slnx', 'Directory.Build.props',
        'docs/reviews/evidence/B0-A-TRACE-01', 'docs/reviews/evidence/B0-A-TRACE-Q1',
        'docs/reviews/probes/B0-A-TRACE-Q1', 'docs/reviews/B0-A-TRACE-Q1-independent-qualification.md']
    assert not git('diff', BASE, HEAD, '--', *protected)
    assert not git('diff', '9abf5e3', HEAD, '--', 'src/BoltSharp', 'BoltSharp.slnx', 'Directory.Build.props')
    result = {'head': HEAD, 'tree': TREE, 'baseline_tree': git('rev-parse', BASE+'^{tree}').decode().strip(),
        'ancestry': git('log', '-3', '--format=%H %T %P').decode(), 'remediation_change_set': changed,
        'production_and_historical_paths_unchanged': protected, 'manifests': {}, 'historical_trx': []}
    for name in ['B0-A-TRACE-01', 'B0-A-TRACE-Q1', 'B0-A-TRACE-R1']:
        directory = ROOT / 'docs/reviews/evidence' / name
        manifest = json.loads((directory / 'sha256.json').read_bytes())
        entries = []
        for entry in manifest['files']:
            rel = 'docs/reviews/evidence/' + name + '/' + entry['path']
            blob, disk = git('cat-file', 'blob', HEAD+':'+rel), (ROOT / rel).read_bytes()
            assert digest(blob) == entry['sha256'].upper(), rel
            assert digest(disk) == entry['sha256'].upper(), rel
            assert len(blob) == entry['bytes'] == len(disk)
            entries.append(entry['path'])
        listed = set(git('ls-tree', '-r', '--name-only', HEAD, '--', str(directory.relative_to(ROOT)).replace('\\','/')).decode().splitlines())
        actual = {str((directory / p).relative_to(ROOT)).replace('\\','/') for p in entries+['sha256.json']}
        assert listed == actual, (name, listed ^ actual)
        result['manifests'][name] = {'verified_git_and_disk': len(entries), 'missing_or_unlisted': 0}
        for path in directory.rglob('*.trx'):
            result['historical_trx'].append(parse_trx(path))
    save('integrity.json', result)
    print('Integrity:', result['manifests'], flush=True)

def parse_trx(path):
    root = ET.parse(path).getroot()
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    results = root.findall('.//t:UnitTestResult', ns)
    counts = {}
    for result in results: counts[result.attrib['outcome']] = counts.get(result.attrib['outcome'], 0) + 1
    counters = root.find('.//t:Counters', ns)
    assert int(counters.attrib['total']) == len(results)
    assert int(counters.attrib['passed']) == counts.get('Passed', 0)
    assert int(counters.attrib['failed']) == counts.get('Failed', 0)
    return {'path': str(path.relative_to(ROOT)).replace('\\','/'), 'counts': counts, 'counters': counters.attrib,
        'not_executed': [r.attrib['testName'] for r in results if r.attrib['outcome'] == 'NotExecuted']}

def run(name, args, authentic=False):
    env = dict(os.environ)
    # Ignore any inherited secret-bearing integration or evidence configuration.
    for key in list(env):
        if key.startswith('BOLTSHARP_') or key.startswith('NEO4J_'): env.pop(key)
    if authentic:
        directory = EVIDENCE / 'authentic-reproduction'
        directory.mkdir(exist_ok=True)
        env['BOLTSHARP_TRACE01_EVIDENCE_DIR'] = str(directory)
    start = time.time()
    print('RUN', name, subprocess.list2cmdline(args), flush=True)
    with (EVIDENCE / (name+'.txt')).open('w', encoding='utf-8') as f:
        result = subprocess.run(args, cwd=ROOT, env=env, stdout=f, stderr=subprocess.STDOUT)
    entry = {'name': name, 'argv': args, 'command': subprocess.list2cmdline(args),
        'exit_code': result.returncode, 'elapsed_seconds': round(time.time()-start,3),
        'started_utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime(start)),
        'evidence_directory': str(EVIDENCE / 'authentic-reproduction') if authentic else None}
    path = EVIDENCE / 'trx' / (name+'.trx')
    if path.exists(): entry['trx'] = parse_trx(path)
    text = (EVIDENCE / (name+'.txt')).read_text(encoding='utf-8')
    entry['warning_lines'] = [line for line in text.splitlines() if 'warning ' in line.lower()]
    COMMANDS.append(entry); save('commands.json', COMMANDS)
    print('RESULT', name, result.returncode, entry.get('trx', {}).get('counts'), flush=True)
    return result.returncode

def test(name, project, extra=(), authentic=False):
    return run(name, ['dotnet', 'test', project, '-c', 'Debug',
        '--logger', 'trx;LogFileName='+name+'.trx', '--results-directory', str(EVIDENCE / 'trx'), *extra], authentic)

def controls():
    directory = EVIDENCE / 'negative-control'; directory.mkdir(exist_ok=True)
    old = git('cat-file', 'blob', BASE+':'+SOURCE)
    (directory / 'PreFixListener.cs').write_bytes(old)
    current = (ROOT / SOURCE).read_bytes()
    # Exactly one call is wrapped. No try/catch, cleanup or classification is altered.
    needle = b'client.GetStream()'
    assert current.count(needle) == 1
    hooked = current.replace(needle, b'global::BoltSharp.Qualification.B0ATRACEQ2.ReviewerHooks.GetStream(client)')
    (directory / 'GetStreamHookListener.cs').write_bytes(hooked)
    save('source-controls.json', {'pre_fix_source_commit': BASE,
        'pre_fix_sha256': digest(old), 'current_working_source_sha256': digest(current),
        'hooked_source_sha256': digest(hooked), 'transformation': 'one client.GetStream() expression replaced by callback wrapper; otherwise byte-identical',
        'limits': 'Reflection and injected NetworkStream exceptions control handler phases. Socket faults are injected, not claims of native OS outage reproduction.'})

def suites():
    run('environment', ['dotnet', '--info'])
    run('build', ['dotnet', 'build', 'BoltSharp.slnx', '-c', 'Debug'])
    test('solution', 'BoltSharp.slnx', ['--no-build'])
    test('driver', DRIVER)
    for short, directory, filename in [
        ('b2a-q2','B2-A-Q2','B2AQ2'), ('b2b-q1','B2-B-Q1','B2BQ1'),
        ('b2b-q2','B2-B-Q2','B2BQ2'), ('b2c-q2','B2-C-Q2','B2CQ2'),
        ('q1','B0-A-TRACE-Q1','B0ATRACEQ1')]:
        test(short, f'docs/reviews/probes/{directory}/{filename}.Probes.csproj')
    test('t10', 'docs/reviews/probes/B0-A-TRACE-Q1/B0ATRACEQ1.Probes.csproj', ['--no-build', '--filter', 'FullyQualifiedName~T10_'])
    test('r1-shutdown', DRIVER, ['--no-build', '--filter', 'FullyQualifiedName~ListenerShutdownRegressionTests'])
    test('authentic', DRIVER, ['--no-build', '--filter', 'FullyQualifiedName~HandshakeTraceProbeTests'], authentic=True)
    test('q2', Q2)
    control = EVIDENCE / 'negative-control'
    test('q2-prefixed-control', Q2, ['-p:ListenerSource='+str(control / 'PreFixListener.cs'),
        '--filter', 'FullyQualifiedName~Q01_|FullyQualifiedName~Q02_|FullyQualifiedName~Q12_|FullyQualifiedName~Q14_|FullyQualifiedName~Q11_'])
    test('q2-getstream-hook', Q2, ['-p:ListenerSource='+str(control / 'GetStreamHookListener.cs'),
        '-p:ReviewerHooks=true', '--filter', 'FullyQualifiedName~Q19_|FullyQualifiedName~Q20_'])
    # Restore ordinary exact-source probe build after control builds.
    run('q2-final-build', ['dotnet', 'build', Q2, '-c', 'Debug'])

if __name__ == '__main__':
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    (EVIDENCE / '.gitattributes').write_text('* -text\n', encoding='utf-8')
    (EVIDENCE / '.gitignore').write_text('!*.trx\n', encoding='utf-8')
    integrity(); controls(); suites()
