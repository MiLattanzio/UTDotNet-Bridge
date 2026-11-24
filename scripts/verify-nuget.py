#!/usr/bin/env python3
"""Check the release archives and run a consumer outside the source tree."""
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parent.parent
feed = Path(sys.argv[1]).resolve()
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
names = ['UTDotNet.' + part for part in ('Core', 'Diagnostics', 'UbuntuTouch', 'Avalonia')]
expected = {f'{name}.{version}.{suffix}' for name in names for suffix in ('nupkg', 'snupkg')}
actual = {p.name for p in feed.glob('*.nupkg')} | {p.name for p in feed.glob('*.snupkg')}
if actual != expected:
    raise SystemExit(f'Unexpected release archive set: {sorted(actual ^ expected)}')

for name in names:
    with zipfile.ZipFile(feed / f'{name}.{version}.nupkg') as package:
        files = set(package.namelist())
        manifest = ET.fromstring(package.read(f'{name}.nuspec'))
        ns = {'n': manifest.tag.split('}')[0].removeprefix('{')}
        metadata = manifest.find('n:metadata', ns)
        assert metadata.findtext('n:id', namespaces=ns) == name
        assert metadata.findtext('n:version', namespaces=ns) == version
        assert metadata.findtext('n:license', namespaces=ns) == 'MIT'
        assert metadata.findtext('n:readme', namespaces=ns) == 'README.md'
        assert metadata.findtext('n:authors', namespaces=ns) == 'Mi Lattanzio'
        repository = metadata.find('n:repository', ns)
        assert repository.get('url') == 'https://github.com/MiLattanzio/UTDotNet-Bridge'
        assert re.fullmatch('[0-9a-f]{40}', repository.get('commit', ''))
        assert repository.get('commit') == commit, (name, repository.get('commit'), commit)
        assert {'README.md', 'LICENSE', f'lib/net10.0/{name}.dll', f'lib/net10.0/{name}.xml'} <= files
        libraries = [f for f in files if f.endswith('.dll')]
        assert libraries == [f'lib/net10.0/{name}.dll'], libraries
        for dependency in metadata.findall('.//n:dependency', ns):
            if dependency.get('id').startswith('UTDotNet.'):
                assert dependency.get('version') == version, dependency.attrib
    with zipfile.ZipFile(feed / f'{name}.{version}.snupkg') as symbols:
        assert f'lib/net10.0/{name}.pdb' in symbols.namelist()
    print(f'Validated {name} {version}', flush=True)

# A single package reference must provide all four libraries and their runtime dependencies.
# A fresh package cache also prevents a previous build of this version masking a bad archive.
with tempfile.TemporaryDirectory(prefix='utdotnet-consumer-') as directory:
    consumer = Path(directory)
    (consumer / 'Consumer.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><PackageReference Include="UTDotNet.Avalonia" Version="{version}" /></ItemGroup>
</Project>''')
    configuration = ET.Element('configuration')
    sources = ET.SubElement(configuration, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='release', value=str(feed))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(configuration, 'packageSourceMapping')
    local = ET.SubElement(mapping, 'packageSource', key='release')
    ET.SubElement(local, 'package', pattern='UTDotNet.*')
    public = ET.SubElement(mapping, 'packageSource', key='nuget.org')
    ET.SubElement(public, 'package', pattern='*')
    ET.ElementTree(configuration).write(consumer / 'NuGet.Config', encoding='utf-8')
    (consumer / 'Program.cs').write_text('''using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using System.Threading;
using System.Threading.Tasks;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;
using UTDotNet.Avalonia;
var platform = new PlatformDetector().Detect();
var log = new DiagnosticLog();
log.Write("Consumer", platform.MobileEnvironment.ToString());
using var session = new MaliitSession();
using var bridge = new MaliitInputBridge(log);
// Compile the new public API without constructing a view outside an Avalonia UI thread.
Func<TextBox, DiagnosticLog, TextClipboardBridge> attachClipboard = (field, diagnostics) => new(field, diagnostics);
ITextClipboard nativeClipboard = new ContentHubClipboard(log);
Func<TextBox, DiagnosticLog, ITextClipboard, TextClipboardBridge> attachNativeClipboard =
    (field, diagnostics, clipboard) => new(field, diagnostics, clipboard);
Func<string, CancellationToken, Task<bool>> writeNativeClipboard = nativeClipboard.WriteTextAsync;
Func<CancellationToken, Task<string>> readNativeClipboard = nativeClipboard.ReadTextAsync;
Func<IClipboard, string, DiagnosticLog, CancellationToken, Task<bool>> writeClipboard = ClipboardTextWriter.TryWriteAsync;
GC.KeepAlive(attachClipboard);
GC.KeepAlive(attachNativeClipboard);
GC.KeepAlive(writeNativeClipboard);
GC.KeepAlive(readNativeClipboard);
GC.KeepAlive(writeClipboard);
var inset = MaliitLayout.BottomInset(new InputArea(0, 600, 400, 200), new PixelRect(0, 0, 400, 800), 2);
if (log.Snapshot().Count != 1 || inset != 100 || bridge.IsAvailable) throw new Exception("Consumer smoke failed");
Console.WriteLine("NuGet consumer: all four libraries loaded and clipboard APIs resolved successfully.");
''')
    subprocess.run(['dotnet', 'restore', '--configfile', 'NuGet.Config', '--packages', str(consumer / 'packages')], cwd=consumer, check=True)
    assets = json.loads((consumer / 'obj/project.assets.json').read_text())
    for name in names:
        assert assets['libraries'][f'{name}/{version}']['type'] == 'package'
    subprocess.run(['dotnet', 'run', '--no-restore', '-c', 'Release', '-p:UseSharedCompilation=false'], cwd=consumer, check=True)
