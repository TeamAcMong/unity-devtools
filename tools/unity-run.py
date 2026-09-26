#!/usr/bin/env python3
"""Run Unity in batch mode on a project, with a time limit, and report errors / test results.

    python tools/unity-run.py import  [--unity 6000|2022] [--project P]           open the project once (import, .meta, compile)
    python tools/unity-run.py method  <Namespace.Class.Method> [--unity ..] [--project P]
    python tools/unity-run.py test    editmode|playmode [--unity ..] [--project P] [--category C] [--graphics] [--filter F]

--project defaults to this repo (dev project, Unity 6000); for 2022 use the temp project made by make-temp-project-2022.py.
Refuses to start when another Unity has the project open (Temp/UnityLockfile held). Logs and test XML go to
tools/.cache/runs/. Exit code: Unity's, or 1 when a test failed / an error line was found.
IMGUI-dependent tests (category DevTools.UI) need --graphics: with -nographics IMGUI does not lay out.
"""
import argparse, os, re, subprocess, sys, time
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
HUB = r'C:\Program Files\Unity\Hub\Editor'
VERSIONS = {'2022': '2022.3.62f2', '6000': '6000.5.7f1'}
RUNS = os.path.join(ROOT, 'tools', '.cache', 'runs')


def locked(project):
    lf = os.path.join(project, 'Temp', 'UnityLockfile')
    if not os.path.exists(lf):
        return False
    try:
        with open(lf, 'a'):
            return False
    except OSError:
        return True


def run(unity_key, project, extra, timeout, graphics, label):
    exe = os.path.join(HUB, VERSIONS[unity_key], 'Editor', 'Unity.exe')
    if locked(project):
        print('project is open in another Unity: ' + project)
        sys.exit(2)
    os.makedirs(RUNS, exist_ok=True)
    log = os.path.join(RUNS, '%s_%s_%d.log' % (label, unity_key, int(time.time())))
    cmd = [exe, '-batchmode'] + ([] if graphics else ['-nographics']) + ['-projectPath', project, '-logFile', log] + extra
    print('>', ' '.join('"%s"' % c if ' ' in c else c for c in cmd))
    t0 = time.time()
    try:
        code = subprocess.run(cmd, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        print('TIMEOUT after %d s (log %s)' % (timeout, log))
        return 124, log
    text = open(log, encoding='utf-8', errors='replace').read() if os.path.exists(log) else ''
    errs = [l for l in text.splitlines() if re.search(r'error CS\d+|Exception:|Scripts have compiler errors|executeMethod class .* could not be found', l)]
    print('unity exit %d in %.0f s | log %s' % (code, time.time() - t0, log))
    for l in errs[:30]:
        print('   ', l[:300])
    return (1 if errs and code == 0 else code), log


def report(xml_path):
    if not os.path.exists(xml_path):
        print('no test results written: ' + xml_path)
        return 1
    root = ET.parse(xml_path).getroot()
    print('tests: total %s, passed %s, failed %s, skipped %s' % (root.get('total'), root.get('passed'), root.get('failed'), root.get('skipped')))
    bad = 0
    for tc in root.iter('test-case'):
        if tc.get('result') == 'Failed':
            bad += 1
            msg = tc.find('failure/message')
            print('  FAIL', tc.get('fullname'))
            if msg is not None and msg.text:
                print('       ', msg.text.strip().replace('\n', '\n        ')[:800])
    return 1 if bad or root.get('result', '').startswith('Failed') else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('what', choices=['import', 'method', 'test'])
    ap.add_argument('arg', nargs='?')
    ap.add_argument('--unity', default='6000')
    ap.add_argument('--project', default=None)
    ap.add_argument('--category', default=None)
    ap.add_argument('--filter', default=None)
    ap.add_argument('--graphics', action='store_true')
    ap.add_argument('--timeout', type=int, default=1800)
    a = ap.parse_args()
    project = os.path.abspath(a.project or (ROOT if a.unity == '6000' else os.path.join(ROOT, 'tools', '.cache', 'temp-2022')))
    if a.what == 'import':
        code, _ = run(a.unity, project, ['-quit'], a.timeout, a.graphics, 'import')
        sys.exit(code)
    if a.what == 'method':
        code, _ = run(a.unity, project, ['-executeMethod', a.arg, '-quit'], a.timeout, a.graphics, 'method')
        sys.exit(code)
    platform = {'editmode': 'EditMode', 'playmode': 'PlayMode'}[a.arg]
    xml = os.path.join(RUNS, 'results_%s_%s.xml' % (a.arg, a.unity))
    if os.path.exists(xml):
        os.remove(xml)
    extra = ['-runTests', '-testPlatform', platform, '-testResults', xml]
    if a.category:
        extra += ['-testCategory', a.category]
    if a.filter:
        extra += ['-testFilter', a.filter]
    code, _ = run(a.unity, project, extra, a.timeout, a.graphics, 'test-' + a.arg)
    sys.exit(max(report(xml), 0 if code in (0, 2) else code) if code != 124 else 124)


if __name__ == '__main__':
    main()
