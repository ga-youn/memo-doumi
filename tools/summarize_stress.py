"""반복 사용 시험 결과 요약.

    python tools/summarize_stress.py build/stress-실행폴더 [--quiet]

latency.csv, metrics.csv, handles.csv 를 읽어 summary.json 을 만들고 표로 찍는다.
'안정 구간' 은 창 100개를 한 바퀴 다 연 뒤(120회째 이후)를 말한다.
"""
import csv
import json
import math
import statistics
import sys
from pathlib import Path

STABLE_FROM = 120
MIB = 1024 * 1024

def read(path):
    if not path.exists():
        return []
    with path.open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))

def slope_per_hour(points):
    """최소제곱 직선의 기울기 (시간당 증가량)."""
    if len(points) < 3:
        return None
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    mx, my = statistics.fmean(xs), statistics.fmean(ys)
    den = sum((x - mx) ** 2 for x in xs)
    return None if den == 0 else sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / den * 3600

def series(rows, key, scale=1.0):
    vals = [float(r[key]) / scale for r in rows]
    pts = [(float(r['seconds']), float(r[key]) / scale) for r in rows]
    return {
        'first': round(vals[0], 2), 'last': round(vals[-1], 2),
        'min': round(min(vals), 2), 'max': round(max(vals), 2),
        'mean': round(statistics.fmean(vals), 2),
        'slope_per_hour': None if slope_per_hour(pts) is None else round(slope_per_hour(pts), 2),
    }

def main():
    p = Path(sys.argv[1])
    quiet = '--quiet' in sys.argv

    groups = {}
    for r in read(p / 'latency.csv'):
        groups.setdefault(r['operation'], []).append(float(r['ms']))
    latency = {}
    for k, v in groups.items():
        v.sort()
        latency[k] = {'count': len(v), 'median_ms': round(statistics.median(v), 3),
                      'p95_ms': round(v[math.ceil(len(v) * .95) - 1], 3), 'max_ms': round(max(v), 3)}

    m = read(p / 'metrics.csv')
    check = [r for r in m if r['phase'] != 'running']
    stable = [r for r in m if int(r['cycle']) >= STABLE_FROM]
    stable_check = [r for r in check if int(r['cycle']) >= STABLE_FROM]
    resources = {}
    for name, rows in (('stable_all_samples', stable), ('stable_checkpoints', stable_check)):
        if not rows:
            continue
        resources[name] = {
            'samples': len(rows),
            'working_mib': series(rows, 'working_bytes', MIB),
            'private_mib': series(rows, 'private_bytes', MIB),
            'managed_mib': series(rows, 'managed_bytes', MIB),
            'handles': series(rows, 'handles'),
            'gdi': series(rows, 'gdi'),
            'user': series(rows, 'user'),
            'cached_forms': series(rows, 'cached_forms'),
        }
        if 'threads' in rows[0]:
            resources[name]['threads'] = series(rows, 'threads')
    cpu = None
    if len(m) >= 2:
        dt = float(m[-1]['seconds']) - float(m[0]['seconds'])
        cpu = round(100 * (float(m[-1]['cpu_seconds']) - float(m[0]['cpu_seconds'])) / dt, 2) if dt > 0 else None

    types = {}
    for r in read(p / 'handles.csv'):
        if int(r['cycle']) < STABLE_FROM:
            continue
        types.setdefault(r['type'], []).append((int(r['cycle']), int(r['count'])))
    handle_types = {t: {'first': v[0][1], 'last': v[-1][1], 'min': min(c for _, c in v), 'max': max(c for _, c in v)}
                    for t, v in sorted(types.items())}

    result = {'latency': latency, 'cpu_one_core_percent': cpu, 'resources': resources,
              'handle_types_stable': handle_types, 'checkpoints': check}
    (p / 'summary.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    if quiet:
        return

    print(p.name)
    print('%-24s %6s %9s %9s %10s' % ('operation', 'n', 'median', 'p95', 'max'))
    for k, v in latency.items():
        print('%-24s %6d %9.2f %9.2f %10.2f' % (k, v['count'], v['median_ms'], v['p95_ms'], v['max_ms']))
    print('CPU (one core): %s%%' % cpu)
    for name, res in resources.items():
        print('-- %s (%d)' % (name, res['samples']))
        for key, s in res.items():
            if key == 'samples':
                continue
            print('   %-13s first %9.2f  last %9.2f  min %9.2f  max %9.2f  mean %9.2f  slope/h %s'
                  % (key, s['first'], s['last'], s['min'], s['max'], s['mean'], s['slope_per_hour']))
    print('-- handle types in stable phase (first / last / min / max)')
    for t, v in handle_types.items():
        print('   %-22s %4d %4d %4d %4d' % (t, v['first'], v['last'], v['min'], v['max']))

if __name__ == '__main__':
    main()
