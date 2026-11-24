"""Prepare the scene shadow graph only; run it after the world batch is accepted."""
import hashlib
import json
from pathlib import Path

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
CANDIDATE = ROOT / 'work/line-camera-scene-next-20261113'
manifest_path = CANDIDATE / 'candidate-manifest.json'
assert hashlib.sha256(manifest_path.read_bytes()).hexdigest() == 'bc3e4e119041ad1eef20a0efb0a80ab4f0f0c92fb1b74375d8fafc64710aa006'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
rows = manifest['files']; assert len(rows) == 10
for row in rows:
    assert hashlib.sha256((CANDIDATE / row['candidatePath']).read_bytes()).hexdigest() == row['candidateSha256']
def new(name, text):
    target = WORK / name
    if target.exists():
        backup = WORK / 'unbuilt-r1-projects' / name
        backup.parent.mkdir(exist_ok=True)
        with backup.open('xb') as stream: stream.write(target.read_bytes())
    with target.open('w', encoding='utf-8') as stream: stream.write(text)
new('Directory.Build.props', '<Project><Import Project="../../Directory.Build.props" /></Project>\n')
new('NuGet.Config', '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources></configuration>\n')
for name, folder, framework, output in (
    ('Runtime.SceneShadow', 'src/RizToolbox.Runtime', 'net6.0', ''),
    ('Adapter.SceneShadow', 'src/RizToolbox.Adapter', 'net6.0', ''),
    ('Judgements.SceneShadow', 'tests/RizToolbox.Judgements.Tests', 'net8.0', '<OutputType>Exe</OutputType>')):
    selected = [row['candidatePath'] for row in rows if row['candidatePath'].startswith(folder + '/')]
    excludes = ['../../' + folder + '/bin/**', '../../' + folder + '/obj/**']
    excludes += ['../../' + path for path in selected]
    if name.startswith('Judgements'): excludes.append('../../' + folder + '/Program.cs')
    references = []
    for product in ('Sdk', 'Core'):
        references.append(f'<Reference Include="RizToolbox.{product}"><HintPath>../../docs/validation/line-ring-world-read-20261113/binaries/production/RizToolbox.{product}.dll</HintPath></Reference>')
    if not name.startswith('Runtime'): references.append('<ProjectReference Include="Runtime.SceneShadow.csproj" />')
    if name.startswith('Judgements'): references.append('<ProjectReference Include="Adapter.SceneShadow.csproj" />')
    includes = [f'<Compile Include="../line-camera-scene-next-20261113/{path}" />' for path in selected]
    if name.startswith('Judgements'): includes.append('<Compile Include="Program.cs" />')
    assembly = folder.rsplit('/', 1)[1]
    new(name + '.csproj', f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>{framework}</TargetFramework><AssemblyName>{assembly}</AssemblyName>{output}<EnableDefaultCompileItems>false</EnableDefaultCompileItems><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup>
  <ItemGroup>
    <Compile Include="../../{folder}/**/*.cs" Exclude="{';'.join(excludes)}" />
    {' '.join(includes)}
    {' '.join(references)}
  </ItemGroup>
</Project>
''')
new('Program.cs', '''using RizToolbox.Judgements.Tests.LineRingSource;
using RizToolbox.Sdk;
if (args.Length != 2 || !int.TryParse(args[1], out var index)) return 2;
var cases = args[0] == "source" ? NativeSeekLineRingSourceChecks.Cases : args[0] == "writer" ? NativeSeekLineRingSourceChecks.EndpointWriterCases : Array.Empty<(string Name, Action Run)>();
if (index < 1 || index > cases.Length) return 2;
var item = cases[index - 1]; Console.WriteLine("RUN " + item.Name);
try { item.Run(); }
catch (Exception error) { Console.Error.WriteLine("FAIL " + item.Name + ": " + error); return 1; }
Console.WriteLine("PASS " + item.Name); Console.WriteLine("Scene shadow checks: 1/1 passed."); return 0;
sealed class QuietLog : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
''')
print('Prepared the isolated scene shadow projects; no build/test/main edit.')
