#!/usr/bin/env python3
"""Builds a lesson's pictures and its summary poster from a lesson plan (JSON) and one text-free picture grid.

The image model never draws text (it garbles Malayalam). It draws a 3x2 grid of pictures only; this script
crops the panels and typesets every word itself with real fonts (Baloo Chettan 2 / Noto Sans Malayalam,
shaped by raqm), so conjuncts and chillu letters come out exactly as written.

  python3 compose_lesson.py lesson.json grid.png outdir/ [N=picture.png ...]   (N=3 replaces panel 3 with a redrawn picture)
  -> outdir/pic-0.jpg (hero) ... pic-N.jpg (one per section), outdir/poster.jpg, outdir/body.md
"""
import json, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, features

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.environ.get("FONT_DIR", os.path.join(HERE, "fonts"))
assert features.check("raqm"), "Pillow needs raqm for correct Malayalam shaping"

NAVY = (20, 48, 107)
INK = (33, 37, 41)
ACCENTS = [((52, 120, 246), (230, 240, 255)), ((34, 160, 90), (228, 247, 236)), ((245, 140, 30), (255, 242, 226)),
           ((130, 80, 200), (242, 234, 252)), ((20, 160, 170), (226, 246, 247)), ((230, 70, 120), (253, 233, 241))]


def font(size, weight="Bold", body=False):
    f = ImageFont.truetype(os.path.join(FONTS, "NotoSansMalayalam.ttf" if body else "BalooChettan2.ttf"), size,
                           layout_engine=ImageFont.Layout.RAQM)
    try:
        f.set_variation_by_name(weight.encode())
    except Exception:
        pass
    return f


def wrap(draw, text, f, width):
    words, lines, cur = text.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if draw.textlength(t, font=f) <= width or not cur:
            cur = t
        else:
            lines.append(cur); cur = w
    if cur:
        lines.append(cur)
    return lines


def fit(draw, text, width, max_lines, size, weight="Bold", body=False, min_size=18):
    while size > min_size:
        f = font(size, weight, body)
        lines = wrap(draw, text, f, width)
        if len(lines) <= max_lines and all(draw.textlength(l, font=f) <= width for l in lines):
            return f, lines
        size -= 2
    f = font(min_size, weight, body)
    return f, wrap(draw, text, f, width)[:max_lines]


def split_grid(grid, cols=3, rows=2):
    """Finds the panels by their white gutters (falls back to an even split) and returns them square-cropped."""
    g = grid.convert("RGB")
    W, H = g.size
    gray = g.convert("L")
    px = gray.load()

    def bands(length, other, along_x):
        white = []
        for i in range(length):
            s = 0
            for j in range(0, other, 4):
                s += px[i, j] if along_x else px[j, i]
            white.append(s / (other / 4) > 242)
        return white

    def cuts(white, n, length):
        runs, start = [], None
        for i, w in enumerate(white + [False]):
            if w and start is None:
                start = i
            elif not w and start is not None:
                runs.append((start, i)); start = None
        inner = [r for r in runs if r[0] > length * 0.12 and r[1] < length * 0.88]
        inner.sort(key=lambda r: r[1] - r[0], reverse=True)
        inner = sorted(inner[:n - 1])
        if len(inner) != n - 1:
            return [round(length * k / n) for k in range(n + 1)], None
        edges = [0]
        for a, b in inner:
            edges += [a, b]
        edges.append(length)
        return None, [(edges[2 * k], edges[2 * k + 1]) for k in range(n)]

    even_x, xs = cuts(bands(W, H, True), cols, W)
    even_y, ys = cuts(bands(H, W, False), rows, H)
    if xs is None:
        xs = [(even_x[k], even_x[k + 1]) for k in range(cols)]
    if ys is None:
        ys = [(even_y[k], even_y[k + 1]) for k in range(rows)]
    panels = []
    for (y0, y1) in ys:
        for (x0, x1) in xs:
            p = g.crop((x0, y0, x1, y1))
            bbox = Image.eval(p.convert("L"), lambda v: 255 if v < 245 else 0).getbbox()
            if bbox:
                p = p.crop(bbox)
            s = min(p.size)
            p = p.crop(((p.width - s) // 2, (p.height - s) // 2, (p.width - s) // 2 + s, (p.height - s) // 2 + s))
            panels.append(p)
    return panels


def rounded(draw, box, r, fill, outline=None, width=1):
    draw.rounded_rectangle(box, r, fill=fill, outline=outline, width=width)


def paste_round(canvas, img, box, r):
    x0, y0, x1, y1 = box
    img = img.resize((x1 - x0, y1 - y0), Image.LANCZOS)
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, img.width - 1, img.height - 1), r, fill=255)
    canvas.paste(img, (x0, y0), mask)


def bulb(d, cx, cy, s):
    d.ellipse((cx - s, cy - s * 1.15, cx + s, cy + s * 0.85), fill=(255, 205, 40), outline=(220, 150, 0), width=3)
    d.rectangle((cx - s * 0.45, cy + s * 0.8, cx + s * 0.45, cy + s * 1.35), fill=(90, 100, 120))
    for k in range(3):
        y = cy + s * 0.9 + k * s * 0.17
        d.line((cx - s * 0.45, y, cx + s * 0.45, y), fill=(200, 205, 215), width=2)
    for ang in range(-150, -20, 32):
        import math
        a = math.radians(ang)
        d.line((cx + math.cos(a) * s * 1.35, cy + math.sin(a) * s * 1.4, cx + math.cos(a) * s * 1.8, cy + math.sin(a) * s * 1.85),
               fill=(255, 190, 20), width=4)


def poster(L, pics, path, W=2400, H=1600):
    sections = L["sections"]
    n = len(sections)
    im = Image.new("RGB", (W, H), (247, 250, 255))
    d = ImageDraw.Draw(im)
    # soft background blobs
    bd = ImageDraw.Draw(im)
    bd.ellipse((-260, -260, 420, 380), fill=(214, 238, 255))
    bd.ellipse((W - 420, -220, W + 240, 360), fill=(236, 228, 255))
    bd.ellipse((-200, H - 300, 380, H + 260), fill=(222, 245, 230))
    bd.ellipse((W - 360, H - 320, W + 220, H + 220), fill=(255, 238, 214))
    im = im.filter(ImageFilter.GaussianBlur(40))
    d = ImageDraw.Draw(im)

    # title banner
    tf, tl = fit(d, L["title"], W - 600, 1, 104, "ExtraBold")
    tw = d.textlength(tl[0], font=tf)
    bw = max(tw + 200, 900)
    rounded(d, ((W - bw) // 2, 34, (W + bw) // 2, 200), 60, NAVY)
    d.text((W // 2, 117), tl[0], font=tf, fill="white", anchor="mm")
    sf, sl = fit(d, L.get("subtitle", ""), W - 400, 1, 60, "Bold")
    d.text((W // 2, 262), sl[0] if sl else "", font=sf, fill=NAVY, anchor="mm")

    # cards
    margin, gap = 56, 44
    cw = (W - 2 * margin - gap * (n - 1)) // n
    top, card_h, pts_h = 330, 640, 420
    for i, s in enumerate(sections):
        acc, tint = ACCENTS[i % len(ACCENTS)]
        x0 = margin + i * (cw + gap)
        rounded(d, (x0, top, x0 + cw, top + card_h), 34, tint, acc, 5)
        # number badge + heading
        d.ellipse((x0 + 22, top + 22, x0 + 102, top + 102), fill=acc)
        d.text((x0 + 62, top + 64), str(i + 1), font=font(56, "ExtraBold"), fill="white", anchor="mm")
        hf, hl = fit(d, s["heading"], cw - 140, 2, 50, "ExtraBold", min_size=30)
        hy = top + 62 - (len(hl) - 1) * hf.size * 0.55
        for k, line in enumerate(hl):
            d.text((x0 + 120, hy + k * hf.size * 1.1), line, font=hf, fill=acc, anchor="lm")
        # picture
        ps = min(cw - 48, 400)
        px0 = x0 + (cw - ps) // 2
        paste_round(im, pics[i + 1], (px0, top + 128, px0 + ps, top + 128 + ps), 26)
        # caption
        cf, cl = fit(d, s.get("caption", ""), cw - 40, 2, 38, "SemiBold", min_size=24)
        cy = top + 128 + ps + 22
        for k, line in enumerate(cl):
            d.text((x0 + cw // 2, cy + k * cf.size * 1.25), line, font=cf, fill=INK, anchor="mt")
        # arrow to next card
        if i < n - 1:
            ax, ay = x0 + cw + 6, top + 128 + ps // 2
            d.line((ax, ay, ax + gap - 14, ay), fill=NAVY, width=7)
            d.polygon([(ax + gap - 6, ay), (ax + gap - 22, ay - 13), (ax + gap - 22, ay + 13)], fill=NAVY)
        # key points box
        by = top + card_h + 26
        rounded(d, (x0, by, x0 + cw, by + pts_h), 30, (255, 255, 255), acc, 4)
        kf = font(36, "ExtraBold")
        d.text((x0 + cw // 2, by + 26), lab(L, "key"), font=kf, fill=acc, anchor="mt")
        yy = by + 92
        for p in s.get("points", [])[:3]:
            pf, pl = fit(d, p, cw - 90, 2, 33, "SemiBold", body=True, min_size=24)
            d.ellipse((x0 + 30, yy + pf.size * 0.5 - 8, x0 + 46, yy + pf.size * 0.5 + 8), fill=acc)
            for k, line in enumerate(pl):
                d.text((x0 + 62, yy + k * pf.size * 1.35), line, font=pf, fill=INK)
            yy += len(pl) * pf.size * 1.35 + 18

    # summary bar
    sy = top + card_h + 26 + pts_h + 34
    rounded(d, (margin, sy, W - margin, H - 40), 40, (255, 255, 255), (150, 180, 235), 4)
    bulb(d, margin + 90, (sy + H - 40) // 2, 38)
    d.text((margin + 170, (sy + H - 40) // 2), lab(L, "remember"), font=font(48, "ExtraBold"), fill=NAVY, anchor="lm")
    lx = margin + 170 + d.textlength(lab(L, "remember"), font=font(48, "ExtraBold")) + 30
    d.line((lx, sy + 30, lx, H - 70), fill=(190, 205, 235), width=3)
    mf, ml = fit(d, L["summary"], W - margin - 330 - lx, 1, 44, "Bold", body=True, min_size=34)
    if len(wrap(d, L["summary"], mf, W - margin - 330 - lx)) > 1:
        mf, ml = fit(d, L["summary"], W - margin - 330 - lx, 2, 42, "Bold", body=True, min_size=28)
    my = (sy + H - 40) // 2 - (len(ml) - 1) * mf.size * 0.65
    for k, line in enumerate(ml):
        d.text((lx + 36, my + k * mf.size * 1.3), line, font=mf, fill=INK, anchor="lm")
    d.text((W - margin - 40, (sy + H - 40) // 2), "Googols", font=font(54, "ExtraBold"), fill=NAVY, anchor="rm")
    im.save(path, "JPEG", quality=88, optimize=True, progressive=True)


LABELS = {
    "ml": dict(key="പ്രധാന കാര്യങ്ങൾ", remember="ഓർത്തുവയ്ക്കാം", song="ഓർത്തുവയ്ക്കാൻ ഒരു പാട്ട്", do="ഞാൻ ചെയ്യും",
               talk="പറയാമോ?", glance="ഒറ്റനോട്ടത്തിൽ"),
    "en": dict(key="Key points", remember="Remember", song="A song to remember", do="I will do", talk="Can you tell?",
               glance="At a glance"),
}


def lab(L, k):
    return LABELS.get(L.get("lang", "ml"), LABELS["ml"])[k]


def body_markdown(L):
    """Lesson text for the app. `![caption](pic:N)` lines are shown as the lesson's pictures."""
    out = [f"![{L['title']}](pic:0)", L.get("subtitle", "")]
    for i, s in enumerate(L["sections"]):
        out.append(f"## {i + 1}. {s['heading']}")
        out.append(f"![{s.get('caption', s['heading'])}](pic:{i + 1})")
        out.append("\n".join(s["text"]))
        if s.get("points"):
            out.append("\n".join(f"- {p}" for p in s["points"]))
    if L.get("rhyme"):
        out.append(f"## {L.get('rhyme_title') or lab(L, 'song')}")
        out.append("\n".join(L["rhyme"]))
    if L.get("activity"):
        out.append(f"## {lab(L, 'do')}")
        out.append(L["activity"])
    if L.get("talk"):
        out.append(f"## {lab(L, 'talk')}")
        out.append("\n".join(f"- {t}" for t in L["talk"]))
    out.append(f"## {lab(L, 'remember')}")
    out.append(f"**{L['summary']}**")
    out.append(f"![{lab(L, 'glance')}](pic:poster)")
    return "\n\n".join(x for x in out if x)


if __name__ == "__main__":
    L = json.load(open(sys.argv[1]))
    out = sys.argv[3]
    os.makedirs(out, exist_ok=True)
    pics = split_grid(Image.open(sys.argv[2]))
    for arg in sys.argv[4:]:
        n, path = arg.split("=", 1)
        pics[int(n)] = Image.open(path).convert("RGB")
    for i, p in enumerate(pics[:len(L["sections"]) + 1]):
        p.resize((768, 768), Image.LANCZOS).save(os.path.join(out, f"pic-{i}.jpg"), "JPEG", quality=86, optimize=True)
    poster(L, pics, os.path.join(out, "poster.jpg"))
    open(os.path.join(out, "body.md"), "w").write(body_markdown(L))
    print("ok", out)
