#!/usr/bin/env python3
"""Every file and folder of the package and of Assets/ has a .meta, and no .meta is left without its asset.
A package installed from git is read-only: a missing .meta makes Unity skip the file (and GUIDs differ between machines).

    python tools/check-meta.py
"""
import os, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
TREES = [os.path.join(ROOT, 'Packages', 'com.dreamtech.devtools'), os.path.join(ROOT, 'Assets')]
IGNORED = {'.DS_Store', 'Thumbs.db'}


def main():
    bad = []
    for top in TREES:
        for dp, dns, fns in os.walk(top):
            dns[:] = [d for d in dns if not d.startswith('.') and not d.endswith('~')]
            for name in dns + fns:
                if name in IGNORED or name.endswith('.meta'):
                    continue
                path = os.path.join(dp, name)
                if path != top and not os.path.exists(path + '.meta'):
                    bad.append('missing .meta: ' + os.path.relpath(path, ROOT))
            for name in fns:
                if name.endswith('.meta') and not os.path.exists(os.path.join(dp, name[:-5])):
                    bad.append('orphan .meta: ' + os.path.relpath(os.path.join(dp, name), ROOT))
    for b in bad:
        print(b)
    print('check-meta: ' + ('OK' if not bad else '%d problem(s)' % len(bad)))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
