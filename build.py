"""Build with .NET Framework C# compiler; propagate failure, embed all web assets."""
from pathlib import Path
import os, subprocess, sys, urllib.request, urllib.error, json
root=Path(__file__).resolve().parent
try:
    s=json.load(urllib.request.urlopen('http://localhost:8765/api/state',timeout=2))
    if s.get('live') or s.get('activeDownloads',0):
        sys.exit('Build refused: active transfers')
except urllib.error.URLError:
    pass
csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
args=[str(csc),'/nologo','/codepage:65001','/target:winexe','/optimize+','/out:'+str(root/'AdamDrop.exe'),'/r:System.Windows.Forms.dll','/r:System.Drawing.dll']
for f in (root/'web').rglob('*'):
    if f.is_file() and (f.parent==root/'web' and f.suffix in ('.html','.js','.png','.ico','.shortcut') or f.parent.name=='guide' and f.suffix=='.png'):
        args.append('/resource:'+str(f)+',web.'+str(f.relative_to(root/'web')).replace('\\','.').replace('/','.'))
args.append(str(root/'AdamDrop.cs'))
subprocess.run(args,check=True)
print('BUILD OK:',root/'AdamDrop.exe',(root/'AdamDrop.exe').stat().st_size,'bytes')
