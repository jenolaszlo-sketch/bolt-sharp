"""Preserve initial results; rerun only corrected/extended reviewer probes."""
import importlib.util, json, pathlib
ROOT = pathlib.Path.cwd()
spec = importlib.util.spec_from_file_location('runner', ROOT/'docs/reviews/probes/B0-A-TRACE-Q2/Run-Qualification.py')
r = importlib.util.module_from_spec(spec); spec.loader.exec_module(r)
r.COMMANDS = json.loads((r.EVIDENCE/'commands.json').read_bytes())
r.test('q2-corrected', r.Q2)
control = r.EVIDENCE/'negative-control'
r.test('q2-prefix-corrected', r.Q2, ['-p:ListenerSource='+str(control/'PreFixListener.cs'),
    '--filter', 'FullyQualifiedName~Q01_|FullyQualifiedName~Q02_|FullyQualifiedName~Q12_|FullyQualifiedName~Q14_|FullyQualifiedName~Q11_|FullyQualifiedName~Q15_'])
r.test('q2-hook-corrected', r.Q2, ['-p:ListenerSource='+str(control/'GetStreamHookListener.cs'),
    '-p:ReviewerHooks=true', '--filter', 'FullyQualifiedName~Q19_|FullyQualifiedName~Q20_'])
r.run('q2-corrected-build', ['dotnet', 'build', r.Q2, '-c', 'Debug'])
