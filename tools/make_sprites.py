"""강아지 사진(정면으로 앉은 전신, 투명 배경 PNG)을 통째로 도트로 줄여 캐릭터 프레임을 만든다.

사용법:  python -I tools/make_sprites.py <사진.png> <출력폴더> [미리보기.png]
"""
import os
import sys
from PIL import Image, ImageEnhance

DOG_H = 38

STYLE = "photo"
HOP = 2
COLORS = 10
OUTLINE = (0x8A, 0x7B, 0x74, 255)
PINK = (0xFF, 0x8F, 0xA3, 255)
PINK_DARK = (0xE8, 0x6A, 0x84, 255)
INK = (0x4A, 0x3D, 0x3B, 255)
EYE = (0x2B, 0x23, 0x21, 255)
MUZZLE = (0x7A, 0x69, 0x64, 255)

CLEAN = [(0xFF, 0xFF, 0xFF, 255), (0xF7, 0xF3, 0xEE, 255), (0xE4, 0xDC, 0xD5, 255), (0xCB, 0xBF, 0xB6, 255)]

EYE_Y = (0.18, 0.36)
EYE_SKIP_X = (0.36, 0.62)
FACE_BOTTOM = 0.60
MOUTH = (0.47, 0.57)

def pixelate(photo):
    src = Image.open(photo).convert("RGBA")
    src = src.crop(src.getbbox())
    w = round(DOG_H * src.width / src.height)

    small = src.convert("RGBa").resize((w, DOG_H), Image.LANCZOS).convert("RGBA")
    rgb = small.convert("RGB")
    rgb = ImageEnhance.Brightness(rgb).enhance(1.12)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.25)
    q = rgb.quantize(COLORS, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    return {(x, y): q.getpixel((x, y)) + (255,)
            for y in range(DOG_H) for x in range(w) if small.getpixel((x, y))[3] >= 140}, w

def clean_up(px, w):
    """사진 티를 줄인다: 털은 흰색 계열 세 단계로, 어중간한 그림자는 옅은 그늘 한 가지로, 삐죽한 픽셀은 정리."""
    v = lambda p: sum(p[:3])
    if STYLE != "photo":
        lights = sorted({p for p in px.values() if v(p) > 420}, key=lambda p: -v(p))
        mapping = {p: CLEAN[min(2, i * 3 // max(len(lights), 1))] for i, p in enumerate(lights)}
        px = {k: mapping.get(p, p) for k, p in px.items()}
        deep = v(CLEAN[3])

        face_bottom = FACE_BOTTOM * DOG_H
        px = {k: (CLEAN[3] if v(p) < deep and (v(p) >= 330 or k[1] >= face_bottom) else p) for k, p in px.items()}

    def neighbors(k):
        return sum(((k[0] + dx, k[1] + dy) in px) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))

    for _ in range(2):
        px = {k: p for k, p in px.items() if neighbors(k) >= 2}
        for y in range(DOG_H):
            for x in range(w):
                if (x, y) not in px and neighbors((x, y)) >= 3:
                    px[(x, y)] = CLEAN[1]
    return crisp(px, w) if STYLE in ("crisp", "retouch") else px

def crisp(px, w):
    """사진을 줄일 때 생기는 자잘한 얼룩을 없애 도트 그림처럼 또렷하게 만든다.
    - 털: 주변에서 가장 많은 색으로 맞추기를 두 번 -> 색이 넓은 면으로 정리된다
    - 눈·코·입: 진한 색 두 가지로만 정리하고, 외톨이 점은 지운다
    """
    v = lambda p: sum(p[:3])
    dark = {k for k, p in px.items() if v(p) < 330}
    around = [(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)]

    for _ in range(2):
        nxt = dict(px)
        for (x, y), p in px.items():
            if (x, y) in dark:
                continue
            votes = {}
            for dx, dy in around:
                q = px.get((x + dx, y + dy))
                if q is not None and (x + dx, y + dy) not in dark:
                    votes[q] = votes.get(q, 0) + (2 if (dx, dy) == (0, 0) else 1)
            nxt[(x, y)] = max(votes, key=lambda c: (votes[c], c == p))
        px = nxt

    lonely = {k for k in dark if not any((k[0] + dx, k[1] + dy) in dark for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}
    for k in lonely:
        px[k] = CLEAN[2]

    for k in dark - lonely:
        px[k] = EYE if v(px[k]) < 200 else MUZZLE
    return retouch(px, w) if STYLE == "retouch" else px

STATE = {}

def retouch(px, w):
    """손질: 눈·코·입은 사진에서 위치만 가져와 또렷한 모양으로 다시 그리고, 털 음영은 얼룩이 덜 지게 다듬는다."""
    face_bottom = FACE_BOTTOM * DOG_H
    center = lambda x: EYE_SKIP_X[0] * w <= x < EYE_SKIP_X[1] * w
    in_eye_rows = lambda y: EYE_Y[0] * DOG_H <= y < EYE_Y[1] * DOG_H
    is_dark = lambda p: p in (EYE, MUZZLE)

    eyes = []
    for side in (lambda x: x < EYE_SKIP_X[0] * w, lambda x: x >= EYE_SKIP_X[1] * w):
        pts = [k for k, p in px.items() if p == EYE and side(k[0]) and in_eye_rows(k[1])]
        if pts:
            eyes.append((round(sum(x for x, _ in pts) / len(pts)), round(sum(y for _, y in pts) / len(pts))))
    nose_pts = [k for k, p in px.items() if p == EYE and center(k[0]) and EYE_Y[0] * DOG_H <= k[1] < face_bottom]

    for k, p in list(px.items()):
        if k[1] < face_bottom + 2 and is_dark(p):
            px[k] = CLEAN[0]

    for ex, ey in eyes:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                px[(ex + dx, ey + dy)] = EYE
        px[(ex - 1, ey - 1)] = CLEAN[0]

    if nose_pts:
        top = min(y for _, y in nose_pts)
        upper = [k for k in nose_pts if k[1] <= top + 3]
        cx = round(sum(x for x, _ in upper) / len(upper))
        ny = round(sum(y for _, y in upper) / len(upper)) - 1
        for dx in (-2, -1, 0, 1):
            px[(cx + dx, ny)] = EYE
            px[(cx + dx, ny + 1)] = EYE
        px[(cx - 1, ny + 2)] = EYE
        px[(cx, ny + 2)] = EYE
        px[(cx - 1, ny)] = MUZZLE
        for k in ((cx - 1, ny + 3), (cx, ny + 3), (cx - 2, ny + 4), (cx + 1, ny + 4), (cx - 3, ny + 4), (cx + 2, ny + 4)):
            if k in px:
                px[k] = MUZZLE
        STATE["mouth"] = (cx, ny + 5)

    lighter = {CLEAN[1]: CLEAN[0], CLEAN[2]: CLEAN[1], CLEAN[3]: CLEAN[2]}
    px = {k: lighter.get(p, p) for k, p in px.items()}

    for (x, y), p in list(px.items()):
        if p == CLEAN[0] and y > DOG_H * 0.3 and ((x + 1, y) not in px or (x, y + 1) not in px):
            px[(x, y)] = CLEAN[1]
    return px

def closed_eyes(px, w):
    """눈(아주 어두운 덩어리)을 털 색으로 덮고 가운데 한 줄만 남긴다."""
    is_eye_zone = lambda x, y: EYE_Y[0] * DOG_H <= y < EYE_Y[1] * DOG_H and not (EYE_SKIP_X[0] * w <= x < EYE_SKIP_X[1] * w)
    eyes = [k for k, p in px.items() if is_eye_zone(*k) and sum(p[:3]) < 200]
    out = dict(px)
    if not eyes:
        return out
    top, bottom = min(y for _, y in eyes), max(y for _, y in eyes)
    mid = (top + bottom + 1) // 2

    cover = [k for k, p in px.items()
             if top - 1 <= k[1] <= bottom + 1 and is_eye_zone(*k) and sum(p[:3]) < 580]
    hidden = set(cover)
    for (x, y) in cover:
        found = None
        for r in range(1, 7):
            ring = [(x + dx, y + dy) for dx in range(-r, r + 1) for dy in range(-r, r + 1) if max(abs(dx), abs(dy)) == r]
            near = [px[k] for k in ring if k in px and k not in hidden and sum(px[k][:3]) >= 580]
            if near:
                found = max(set(near), key=near.count)
                break
        out[(x, y)] = found or CLEAN[1]
    for x in sorted({x for x, _ in eyes}):
        out[(x, mid)] = INK
    return out

def tongue(px, w):
    out = dict(px)
    cx, cy = STATE.get("mouth", (int(MOUTH[0] * w), int(MOUTH[1] * DOG_H)))

    shape = [(-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1), (-1, 2), (0, 2), (1, 2), (0, 3)]
    for dx, dy in shape:
        k = (cx + dx, cy + dy)
        if k in out:
            out[k] = PINK_DARK if (dx == 0 and dy in (0, 1)) else PINK
    return out

def frame(px, w, lift=0, squash=1.0):
    """외곽선을 두르고, 발이 항상 같은 바닥에 닿도록 아래에 맞춰 놓는다. lift 만큼 띄우거나 squash 로 납작하게."""
    W, H = w + 2, DOG_H + 2 + HOP
    dog = Image.new("RGBA", (w + 2, DOG_H + 2), (0, 0, 0, 0))
    for (x, y), p in px.items():
        dog.putpixel((x + 1, y + 1), p)
    for y in range(dog.height):
        for x in range(dog.width):
            if dog.getpixel((x, y))[3]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < dog.width and 0 <= ny < dog.height and dog.getpixel((nx, ny))[3] and dog.getpixel((nx, ny)) != OUTLINE:
                    dog.putpixel((x, y), OUTLINE)
                    break
    if squash != 1.0:
        dog = dog.resize((dog.width, round(dog.height * squash)), Image.NEAREST)
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    im.alpha_composite(dog, (0, H - dog.height - lift))
    return im

def main():
    global STYLE, DOG_H
    for a in [a for a in sys.argv if a.startswith("--")]:
        sys.argv.remove(a)
        key, _, val = a[2:].partition("=")
        if key == "style":
            STYLE = val
        elif key == "height":
            DOG_H = int(val)
    photo, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    raw, w = pixelate(photo)
    px = clean_up(raw, w)
    shut = closed_eyes(px, w)
    frames = {
        "stand": frame(px, w),
        "blink": frame(shut, w),
        "wag": frame(px, w, lift=1),
        "walk1": frame(px, w),
        "walk2": frame(px, w, lift=HOP),
        "happy": frame(tongue(px, w), w),
        "sleep": frame(shut, w, squash=0.86),
    }
    for name, im in frames.items():
        im.save(os.path.join(outdir, name + ".png"))
    if len(sys.argv) > 3:
        k = 8
        W, H = frames["stand"].size
        sheet = Image.new("RGBA", ((W * k + 12) * len(frames), H * k + H * 2 + 12), (40, 44, 52, 255))
        for i, im in enumerate(frames.values()):
            sheet.alpha_composite(im.resize((W * k, H * k), Image.NEAREST), (i * (W * k + 12), 0))
            sheet.alpha_composite(im.resize((W * 2, H * 2), Image.NEAREST), (i * (W * k + 12) + W * 3, H * k + 6))
        sheet.save(sys.argv[3])
    print("dog", (w, DOG_H), "frame", frames["stand"].size)

main()
