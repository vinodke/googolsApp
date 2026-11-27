#!/usr/bin/env python3
"""Renders a composed lesson (body.md + pictures from compose_lesson.py) as one self-contained HTML page for review.

Fonts are embedded, and the page uses Noto Sans Malayalam for all text: Baloo Chettan 2 breaks Malayalam
conjuncts in browsers when the page is marked lang="ml" (its 'MAL ' language system lacks the conjunct
features), so it is only used for the poster, which is drawn without a language tag.

  python3 make_preview.py outdir/ "മലയാളം · ക്ലാസ് 1 · പാഠം 1"
"""
import base64, html, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = os.environ.get("FONT_DIR", os.path.join(HERE, "fonts"))


def b64(path):
    return base64.b64encode(open(path, "rb").read()).decode()


def inline(s):
    return re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", html.escape(s))


def build(d, crumb):
    body = open(os.path.join(d, "body.md")).read()
    title = ""
    parts = []
    for b in [x.strip() for x in body.split("\n\n") if x.strip()]:
        m = re.fullmatch(r"!\[(.*?)\]\(pic:(\w+)\)", b)
        if m:
            name, cap = m.group(2), m.group(1)
            title = title or cap
            f = os.path.join(d, "poster.jpg" if name == "poster" else f"pic-{name}.jpg")
            cls = "wide" if name in ("0", "poster") else ""
            parts.append(f'<figure class="{cls}"><img src="data:image/jpeg;base64,{b64(f)}" alt="{html.escape(cap)}">'
                         f'<figcaption>{html.escape(cap)}</figcaption></figure>')
        elif b.startswith("## "):
            parts.append(f"<h3>{inline(b[3:])}</h3>")
        elif all(l.startswith("- ") for l in b.split("\n")):
            parts.append("<ul>" + "".join(f"<li>{inline(l[2:])}</li>" for l in b.split("\n")) + "</ul>")
        else:
            parts.append("<p>" + "<br>".join(inline(l) for l in b.split("\n")) + "</p>")
    font = b64(os.path.join(FONTS, "NotoSansMalayalam.ttf"))
    return f"""<!DOCTYPE html><html lang="ml"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{html.escape(title)} · Googols</title><style>
@font-face{{font-family:"GNoto";src:url(data:font/ttf;base64,{font}) format("truetype");font-weight:100 900}}
body{{margin:0;background:#f4f7fc;font-family:"GNoto",sans-serif;color:#212529;line-height:1.8}}
.wrap{{max-width:780px;margin:24px auto;background:#fff;border-radius:18px;padding:28px 32px;box-shadow:0 2px 14px rgba(20,48,107,.08)}}
.crumb{{font-size:14px;color:#6b7280}} h1{{color:#14306b;font-size:38px;font-weight:800;margin:4px 0 8px}}
h3{{color:#14306b;font-size:26px;font-weight:800;margin:34px 0 8px;border-left:6px solid #3478f6;padding-left:12px}}
p{{font-size:21px;margin:8px 0}} ul{{font-size:19px;background:#f1f6ff;border-radius:12px;padding:12px 16px 12px 40px}}
figure{{margin:14px auto;max-width:440px;text-align:center}} figure.wide{{max-width:100%}}
figure img{{width:100%;border-radius:16px;border:1px solid #e5e7eb}} figcaption{{font-size:15px;color:#6b7280}}
strong{{color:#14306b}}</style></head><body><div class="wrap"><div class="crumb">{html.escape(crumb)}</div>
<h1>{html.escape(title)}</h1>{''.join(parts)}</div></body></html>"""


if __name__ == "__main__":
    d = sys.argv[1]
    open(os.path.join(d, "preview.html"), "w").write(build(d, sys.argv[2] if len(sys.argv) > 2 else ""))
    print("ok", os.path.join(d, "preview.html"))
