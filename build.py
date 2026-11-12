"""Build with Windows .NET Framework csc, or a supplied portable Mono toolchain."""
import argparse
import os
from pathlib import Path
import subprocess

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mono-root', type=Path)
    parser.add_argument('--windres', type=Path)
    parser.add_argument('--tests', action='store_true')
    parser.add_argument('--language', choices=['en', 'ru', 'both'], default='both')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    out = root / 'Build'
    out.mkdir(exist_ok=True)
    refs = ['System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Xml']
    env = os.environ.copy()
    if args.mono_root:
        prefix = args.mono_root.resolve()
        gac = prefix / 'usr/lib/mono/gac'
        libraries = {d.name: next(d.glob('*/*.dll')) for d in gac.iterdir() if d.is_dir()}
        env['MONO_PATH'] = os.pathsep.join([str(prefix/'usr/lib/mono/4.5')] + [str(p.parent) for p in libraries.values()])
        env['LD_LIBRARY_PATH'] = str(prefix/'usr/lib') + os.pathsep + env.get('LD_LIBRARY_PATH', '')
        mono = [str(prefix/'usr/bin/mono-sgen'), '--config', str(prefix/'etc/mono/config')]
        compiler = mono + [str(prefix/'usr/lib/mono/4.5/mcs.exe'), '-noconfig', '-nostdlib',
            '-r:' + str(prefix/'usr/lib/mono/4.5/mscorlib.dll')]
        compiler += ['-r:' + str(libraries[r]) for r in refs]
        # Mono mcs accepts -win32manifest but omits it when generating icon resources.
        # Provide the full native .res so the exact manifest is always embedded.
        windres = args.windres or prefix/'usr/bin/x86_64-w64-mingw32-windres'
        native_resource = out/'app.res'
        subprocess.run([str(windres), '--preprocessor=cpp', '-J', 'rc', '-O', 'res',
            '-i', str(root/'Source/app.rc'), '-o', str(native_resource)], cwd=root, env=env, check=True)
        native_options = ['-win32res:' + str(native_resource)]
    else:
        windir = Path(os.environ.get('WINDIR', r'C:\Windows'))
        compiler = [str(windir/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'), '/nologo']
        compiler += ['-r:' + r + '.dll' for r in refs]
        native_options = ['-win32icon:' + str(root/'Assets/APEIRON.ico'), '-win32manifest:' + str(root/'Source/app.manifest')]
    languages = ['en', 'ru'] if args.language == 'both' else [args.language]
    for language in languages:
        build_language(root, out / language.upper(), compiler, native_options, env,
            mono if args.mono_root else [], language, args.tests)

def build_language(root, out, compiler, native_options, env, runtime, language, tests):
    out.mkdir(exist_ok=True)
    exe = out / 'APEIRON_Global_Mute_Overlay.exe'
    command = compiler + ['-codepage:65001', '-target:winexe', '-platform:x64', '-optimize+', '-out:' + str(exe),
        '-resource:' + str(root/'Assets/APEIRON.ico') + ',APEIRON.ico',
        '-resource:' + str(root/'Assets/APEIRON_icon.png') + ',APEIRON_icon.png']
    if language == 'ru': command.append('-define:APEIRON_RU')
    command += native_options
    command += [str(p) for p in sorted((root/'Source').glob('*.cs'))]
    subprocess.run(command, env=env, check=True)
    print('Built:', exe)
    if tests:
        test = out/'Tests.exe'
        command = compiler + ['-target:exe', '-platform:x64', '-out:' + str(test), '-r:' + str(exe)]
        command += [str(p) for p in sorted((root/'Tests').glob('*.cs'))]
        subprocess.run(command, env=env, check=True)
        subprocess.run(runtime + [str(test)], env=env, check=True)
        subprocess.run([__import__('sys').executable, str(root/'Tests/verify_exe.py'), str(exe)], check=True)

if __name__ == '__main__':
    main()
