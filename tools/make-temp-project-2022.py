#!/usr/bin/env python3
"""Creates tools/.cache/temp-2022: an empty Unity 2022.3 project whose manifest points at the package with file:, to run
the package's EditMode / PlayMode tests on the oldest supported Unity.

    python tools/make-temp-project-2022.py [--bootstrap]
    python tools/unity-run.py test editmode --unity 2022

--bootstrap also opens it once in Unity 2022.3 (import + compile).
Never open this project and the dev project (Unity 6) at the same time: both would write .meta files for new package
files with different GUIDs. After a 2022.3 run, `git status` must show no changed .meta in the package.
"""
import json, os, shutil, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
TEMP = os.path.join(ROOT, 'tools', '.cache', 'temp-2022')
PKG = os.path.join(ROOT, 'Packages', 'com.dreamtech.devtools')


def main():
    os.makedirs(os.path.join(TEMP, 'Assets'), exist_ok=True)
    os.makedirs(os.path.join(TEMP, 'Packages'), exist_ok=True)
    manifest = {
        'dependencies': {
            'com.dreamtech.devtools': 'file:' + PKG.replace('\\', '/'),
            'com.unity.test-framework': '1.1.33',
            'com.unity.ugui': '1.0.0',
            'com.unity.modules.audio': '1.0.0',
            'com.unity.modules.imgui': '1.0.0',
            'com.unity.modules.jsonserialize': '1.0.0',
            'com.unity.modules.screencapture': '1.0.0',
            'com.unity.modules.ui': '1.0.0',
        },
        'testables': ['com.dreamtech.devtools'],
    }
    with open(os.path.join(TEMP, 'Packages', 'manifest.json'), 'w', encoding='utf-8') as f:
        json.dump(manifest, f, indent=2)
    print('temp project: ' + TEMP)
    if '--bootstrap' in sys.argv:
        sys.exit(subprocess.run([sys.executable, os.path.join(ROOT, 'tools', 'unity-run.py'), 'import', '--unity', '2022']).returncode)


if __name__ == '__main__':
    main()
