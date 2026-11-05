"""Decisive run: force compilation so timestamp-preserving copies cannot reuse old probes."""
import importlib.util, json, pathlib
ROOT = pathlib.Path.cwd()
spec = importlib.util.spec_from_file_location('runner', ROOT/'docs/reviews/probes/B0-A-TRACE-Q2/Run-Qualification.py')
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
r.COMMANDS = json.loads((r.EVIDENCE/'commands.json').read_bytes())
assert r.run('q2-verified-rebuild', ['dotnet', 'build', r.Q2, '-c', 'Debug', '--no-incremental']) == 0
r.test('q2-verified', r.Q2, ['--no-build'])
