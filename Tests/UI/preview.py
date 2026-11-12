from pathlib import Path
import os,subprocess,argparse
parser=argparse.ArgumentParser();parser.add_argument('--mono-root',type=Path,required=True);parser.add_argument('--run',action='store_true');parser.add_argument('--binding-example',choices=['none','pair'],default='none');parser.add_argument('--language',choices=['ru','en'],default='ru');args=parser.parse_args()
root=Path(__file__).resolve().parents[2];prefix=args.mono_root.resolve()
gac=prefix/'usr/lib/mono/gac';libraries={d.name:next(d.glob('*/*.dll')) for d in gac.iterdir() if d.is_dir()}
env=os.environ.copy();env['MONO_PATH']=os.pathsep.join([str(prefix/'usr/lib/mono/4.5')]+[str(p.parent) for p in libraries.values()]);env['LD_LIBRARY_PATH']=str(prefix/'usr/lib')
mono=[str(prefix/'usr/bin/mono-sgen'),'--config',str(prefix/'etc/mono/config')]
compiler=mono+[str(prefix/'usr/lib/mono/4.5/mcs.exe'),'-noconfig','-nostdlib','-r:'+str(prefix/'usr/lib/mono/4.5/mscorlib.dll')]+['-r:'+str(libraries[r]) for r in ['System','System.Core','System.Drawing','System.Windows.Forms','System.Xml']]
out=root/'Build/PreviewRuntime'/args.language.upper();out.mkdir(parents=True,exist_ok=True);test=out/'Preview.exe'
if not args.run:
 icon_source=out/'ExportIcon.cs';icon_source.write_text('using System.Drawing; using System.IO; class ExportIcon { static void Main(string[] a) { using(var f=File.Create(a[0])) SystemIcons.Application.Save(f); } }')
 subprocess.run(compiler+['-out:'+str(out/'ExportIcon.exe'),str(icon_source)],check=True,env=env)
 subprocess.run(mono+[str(out/'ExportIcon.exe'),str(out/'compat.ico')],check=True,env=env)
 # Mono cannot decode PNG-compressed Windows ICO entries. Replace only the
 # managed icon resource in this layout-only build; all C# sources are identical.
 preview_app=out/'APEIRON_Global_Mute_Overlay.exe'
 subprocess.run(compiler+(['-define:APEIRON_RU'] if args.language=='ru' else [])+['-codepage:65001','-target:winexe','-out:'+str(preview_app),'-resource:'+str(out/'compat.ico')+',APEIRON.ico','-resource:'+str(root/'Assets/APEIRON_icon.png')+',APEIRON_icon.png']+[str(p) for p in sorted((root/'Source').glob('*.cs'))],check=True,env=env)
 subprocess.run(compiler+['-out:'+str(test),'-r:'+str(preview_app),str(root/'Tests/UI/Preview.cs')],check=True,env=env)
else:
 subprocess.run(mono+[str(test),str(root/'Build/PreviewData'/args.language.upper()),args.binding_example],check=True,env=env)
