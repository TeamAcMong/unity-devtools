#!/usr/bin/env python3
"""Compile-check the package outside Unity with the .NET SDK (Roslyn, C# 9) against the DLLs of each Unity version.

    python tools/compile-check.py [--unity 2022|6000|all] [--parts core,runtime,release,editor,demo] [--max 40]

Parts (each built into tools/.cache/compile-check/<version>/<part>):
  core      Runtime/Core with netstandard only: proves noEngineReferences (no UnityEngine type can sneak in)
  runtime   Runtime/Unity as a development build (DEVELOPMENT_BUILD: the gated code compiles)
  release   Runtime/Unity as a shipping build (no UNITY_EDITOR / DEVELOPMENT_BUILD / DREAMTECH_DEVTOOLS)
  editor    Editor/ with UnityEditor + UNITY_EDITOR
  demo      Assets/Demo/Scripts (dev project only)
On 6000.x, CS0618 / CS0619 (obsolete API) are errors: the package must build warning-free from 2022.3 to Unity 6.
UnityEngine.UI.dll is a package DLL: it is taken from a project's Library/ScriptAssemblies (this dev project for 6000,
a 2022.3 project given by --ui-2022 or the temp project from make-temp-project-2022). Exit code 1 on any failure.
"""
import argparse, glob, os, re, shutil, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PKG = os.path.join(ROOT, 'Packages', 'com.dreamtech.devtools')
CACHE = os.path.join(ROOT, 'tools', '.cache', 'compile-check')
HUB = r'C:\Program Files\Unity\Hub\Editor'
VERSIONS = {'2022': '2022.3.62f2', '6000': '6000.5.7f1'}
DOTNET = shutil.which('dotnet') or os.path.expanduser('~/.dotnet/dotnet.exe')

BASE_DEFINES = ['UNITY_5_3_OR_NEWER', 'UNITY_2017_1_OR_NEWER', 'UNITY_2019_4_OR_NEWER', 'UNITY_2020_1_OR_NEWER', 'UNITY_2021_3_OR_NEWER',
                'UNITY_2022_1_OR_NEWER', 'UNITY_2022_3_OR_NEWER', 'UNITY_STANDALONE', 'UNITY_STANDALONE_WIN', 'ENABLE_MONO',
                'ENABLE_LEGACY_INPUT_MANAGER', 'NET_STANDARD_2_1', 'NET_STANDARD', 'CSHARP_7_3_OR_NEWER']
NEW_DEFINES = ['UNITY_2023_1_OR_NEWER', 'UNITY_2023_2_OR_NEWER'] + ['UNITY_6000_%d_OR_NEWER' % i for i in range(6)]

TEMPLATE = r'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>{name}</AssemblyName><NoStdLib>true</NoStdLib>
<DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<LangVersion>9.0</LangVersion><Nullable>disable</Nullable><DefineConstants>{defines}</DefineConstants><OutputPath>{out}</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath><GenerateAssemblyInfo>false</GenerateAssemblyInfo>
<WarningLevel>4</WarningLevel><TreatWarningsAsErrors>false</TreatWarningsAsErrors><WarningsAsErrors>{warn_errors}</WarningsAsErrors>
<NoWarn>CS1591</NoWarn><BaseIntermediateOutputPath>obj\</BaseIntermediateOutputPath></PropertyGroup>
<ItemGroup>{sources}</ItemGroup><ItemGroup>{refs}</ItemGroup></Project>'''


def engine_refs(ver, editor):
    d = os.path.join(HUB, ver, 'Editor', 'Data')
    refs = [os.path.join(d, 'NetStandard', 'ref', '2.1.0', 'netstandard.dll')]
    refs += glob.glob(os.path.join(d, 'Managed', 'UnityEngine', 'UnityEngine.*Module.dll'))
    refs.append(os.path.join(d, 'Managed', 'UnityEngine', 'UnityEngine.dll'))
    # Unity 6 moved some engine types (PreserveAttribute...) into Unity.*.dll next to the modules
    refs += [f for f in glob.glob(os.path.join(d, 'Managed', 'UnityEngine', 'Unity.*.dll')) if 'Editor' not in os.path.basename(f)]
    if editor:
        mods = glob.glob(os.path.join(d, 'Managed', 'UnityEngine', 'UnityEditor.*Module.dll'))
        # UnityEditor.dll repeats the types of the editor modules where those exist: reference one or the other
        refs += mods if mods else [os.path.join(d, 'Managed', 'UnityEditor.dll')]
    return [r for r in refs if os.path.exists(r)]


def ui_dll(key, args):
    cands = []
    if key == '6000':
        cands.append(os.path.join(ROOT, 'Library', 'ScriptAssemblies', 'UnityEngine.UI.dll'))
    else:
        if args.ui_2022:
            cands.append(os.path.join(args.ui_2022, 'Library', 'ScriptAssemblies', 'UnityEngine.UI.dll'))
        cands.append(os.path.join(ROOT, 'tools', '.cache', 'temp-2022', 'Library', 'ScriptAssemblies', 'UnityEngine.UI.dll'))
    return next((c for c in cands if os.path.exists(c)), None)


def build(key, part, sources, refs, defines, max_err):
    ver = VERSIONS[key]
    name = {'core': 'DreamTech.DevTools', 'runtime': 'DreamTech.DevTools.Unity', 'release': 'DreamTech.DevTools.Unity',
            'editor': 'DreamTech.DevTools.Editor', 'demo': 'DreamTech.DevTools.Demo'}[part]
    d = os.path.join(CACHE, key, part)
    os.makedirs(d, exist_ok=True)
    src = ''.join('<Compile Include="%s" />' % s for s in sources)
    ref = ''.join('<Reference Include="%s"><HintPath>%s</HintPath><Private>false</Private></Reference>' % (os.path.splitext(os.path.basename(r))[0], r) for r in refs)
    warn_errors = 'CS0618;CS0619' if key == '6000' else ''
    proj = os.path.join(d, name + '.csproj')
    open(proj, 'w', encoding='utf-8').write(TEMPLATE.format(name=name, defines=';'.join(defines), out=os.path.join(d, 'bin'),
                                                            warn_errors=warn_errors, sources=src, refs=ref))
    r = subprocess.run([DOTNET, 'build', proj, '-nologo', '-v', 'q', '-clp:NoSummary'], capture_output=True, text=True, encoding='utf-8', errors='replace')
    lines = sorted(set(l.strip() for l in r.stdout.splitlines() if ': error ' in l or ': warning CS' in l))
    errs = [l for l in lines if ': error ' in l]
    print('%-5s %-8s %s  %d error(s), %d warning(s)' % (key, part, 'OK    ' if r.returncode == 0 else 'FAILED', len(errs), len(lines) - len(errs)))
    for l in lines[:max_err]:
        print('     ', re.sub(r' \[[^\]]*\.csproj\]$', '', l.replace(ROOT + os.sep, ''))[:320])
    return os.path.join(d, 'bin', name + '.dll') if r.returncode == 0 else None


def cs(folder):
    return sorted(glob.glob(os.path.join(folder, '**', '*.cs'), recursive=True))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--unity', default='all')
    ap.add_argument('--parts', default='core,runtime,release,editor,demo')
    ap.add_argument('--ui-2022', default=None, help='a Unity 2022.3 project whose Library has UnityEngine.UI.dll')
    ap.add_argument('--max', type=int, default=40)
    args = ap.parse_args()
    keys = list(VERSIONS) if args.unity == 'all' else [args.unity]
    parts = args.parts.split(',')
    ok = True
    for key in keys:
        ver = VERSIONS[key]
        if not os.path.isdir(os.path.join(HUB, ver)):
            print('%s: Unity %s not installed, skipped' % (key, ver))
            continue
        defs = BASE_DEFINES + (NEW_DEFINES if key == '6000' else [])
        core = build(key, 'core', cs(os.path.join(PKG, 'Runtime', 'Core')),
                     [os.path.join(HUB, ver, 'Editor', 'Data', 'NetStandard', 'ref', '2.1.0', 'netstandard.dll')], defs, args.max)
        ok &= core is not None
        if core is None:
            continue
        ui = ui_dll(key, args)
        if ui is None:
            print('%-5s          UnityEngine.UI.dll not found: runtime/editor/demo skipped (open the project once / make the 2022 temp project)' % key)
            continue
        unity_src = cs(os.path.join(PKG, 'Runtime', 'Unity'))
        rt = None
        if 'runtime' in parts or 'editor' in parts or 'demo' in parts:
            rt = build(key, 'runtime', unity_src, engine_refs(ver, False) + [core, ui], defs + ['DEVELOPMENT_BUILD'], args.max)
            ok &= rt is not None
        if 'release' in parts:
            ok &= build(key, 'release', unity_src, engine_refs(ver, False) + [core, ui], defs, args.max) is not None
        if rt and 'editor' in parts:
            ok &= build(key, 'editor', cs(os.path.join(PKG, 'Editor')), engine_refs(ver, True) + [core, ui, rt], defs + ['UNITY_EDITOR', 'UNITY_EDITOR_WIN'], args.max) is not None
        demo = os.path.join(ROOT, 'Assets', 'Demo', 'Scripts')
        if rt and 'demo' in parts and os.path.isdir(demo) and key == '6000':
            ok &= build(key, 'demo', cs(demo), engine_refs(ver, False) + [core, ui, rt], defs + ['DEVELOPMENT_BUILD'], args.max) is not None
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
