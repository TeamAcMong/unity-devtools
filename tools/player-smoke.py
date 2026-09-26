#!/usr/bin/env python3
"""Smoke test of the dev tools in a real player: runs the demo's Windows development build with a -devboot script that
drives the HUD and the standard commands, then prints the DevTools lines of the player log, every error, and the
screenshots the script took (persistentDataPath/DevShots). Build first:

    python tools/unity-run.py method DreamTech.DevTools.Demo.EditorTools.DemoBuild.BuildWindows
    python tools/player-smoke.py [--exe Builds/Demo/DevToolsDemo.exe] [--script "..."] [--out tools/.cache/smoke]

Exit code 1 when a command failed that the script did not expect to fail, or the player logged an exception.
"""
import argparse, glob, os, shutil, subprocess, sys, time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))

# Every line must succeed except those listed in EXPECTED_FAIL.
SCRIPT = [
    'wait 1', 'engine.screenshot true',
    'hud.show-panel quick', 'wait 0.5', 'engine.screenshot true',
    'hud.show-panel economy', 'wait 0.5', 'engine.screenshot true',
    'economy.set-balance coins 12345', 'economy.set-all-items 7',
    'demo.start-level', 'wait 0.3',
    'engine.ui-at-point 0.5 0.75',          # under the open panel: the blocker must be the topmost hit
    'hud.close-panel', 'wait 0.3',
    'engine.ui-at-point 0.5 0.45',          # the demo's TAP button once the panel is closed
    'demo.tap 3', 'level.win', 'wait 0.3',
    'level.win',                            # expected to fail: no level running
    'hud.show-panel scenarios', 'wait 0.5', 'engine.screenshot true',
    'preset "Gift tomorrow"', 'wait 1',
    'demo.start-level', 'level.lose', 'waitfor revive-offer 5', 'ads.rewarded-outcome ForceFail', 'demo.revive-with-ad',
    'ads.rewarded-outcome Normal',
    'hud.show-panel log', 'wait 0.5', 'engine.screenshot true',
    'hud.close-panel', 'wait 0.3', 'engine.screenshot false',
    'wait 1', 'engine.quit',
]
EXPECTED_FAIL = {'level.win': 1}  # the second level.win


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--exe', default=os.path.join(ROOT, 'Builds', 'Demo', 'DevToolsDemo.exe'))
    ap.add_argument('--script', default='; '.join(SCRIPT))
    ap.add_argument('--out', default=os.path.join(ROOT, 'tools', '.cache', 'smoke'))
    ap.add_argument('--timeout', type=int, default=180)
    a = ap.parse_args()
    shots = os.path.join(os.environ.get('USERPROFILE', ''), 'AppData', 'LocalLow', 'DefaultCompany', 'DevToolsDemo', 'DevShots')
    shutil.rmtree(shots, ignore_errors=True)
    shutil.rmtree(a.out, ignore_errors=True)
    os.makedirs(a.out)
    log = os.path.join(a.out, 'player.log')
    cmd = [a.exe, '-screen-width', '540', '-screen-height', '960', '-screen-fullscreen', '0', '-logFile', log, '-devboot', a.script]
    t0 = time.time()
    try:
        code = subprocess.run(cmd, timeout=a.timeout).returncode
    except subprocess.TimeoutExpired:
        print('TIMEOUT: the script did not reach engine.quit')
        code = 124
    text = open(log, encoding='utf-8', errors='replace').read() if os.path.exists(log) else ''
    lines = [l for l in text.splitlines() if l.startswith('[DevTools]')]
    failures = {}
    for l in lines:
        print(l[:260])
        if '-> error:' in l:
            key = l[len('[DevTools] '):].split(' -> ')[0].split(' ')[0]
            failures[key] = failures.get(key, 0) + 1
    unexpected = {k: v - EXPECTED_FAIL.get(k, 0) for k, v in failures.items() if v > EXPECTED_FAIL.get(k, 0)}
    exceptions = [l for l in text.splitlines() if 'Exception' in l and not l.startswith(' ')]
    for p in sorted(glob.glob(os.path.join(shots, '*.png'))):
        shutil.copy(p, a.out)
    print('player exit %d in %.0f s; %d DevTools lines; unexpected failures %s; exceptions %d; screenshots -> %s (%d)'
          % (code, time.time() - t0, len(lines), unexpected or 'none', len(exceptions), a.out, len(glob.glob(os.path.join(a.out, '*.png')))))
    for l in exceptions[:10]:
        print('   ', l[:300])
    sys.exit(1 if unexpected or exceptions or code not in (0,) else 0)


if __name__ == '__main__':
    main()
