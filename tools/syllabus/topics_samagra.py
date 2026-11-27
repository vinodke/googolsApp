#!/usr/bin/env python3
"""Stage 2: for each extracted textbook, find where every chapter starts and list its main topics
(section headings as printed), using the book's own text. Writes ~/syllabus/topics/<key>.json."""
import threading; LOCK = threading.Lock()
import concurrent.futures as cf, glob, json, os, re, sys, time, urllib.request, urllib.error

BOOKS = os.path.expanduser("~/syllabus/books")
OUT = os.path.expanduser("~/syllabus/topics")

def ml_ratio(s):
    letters = [c for c in s if c.isalpha()]
    return sum(1 for c in letters if 'ഀ' <= c <= 'ൿ') / max(1, len(letters))

def malayalam_book(b):
    return b["subject"] in ("Malayalam", "Malayalam (BT)") or (b["medium"] == "ml" and b["subject"] not in ("English", "Hindi"))


def outline(pages):
    """Short lines per page: headings live there. Keeps the prompt small."""
    out = []
    for i, text in enumerate(pages):
        lines = []
        for ln in (text or "").splitlines():
            ln = re.sub(r"\s+", " ", ln).strip()
            if 3 <= len(ln) <= 70 and not ln.endswith((".", ",", ";")) and not re.fullmatch(r"[\d\s.\-–•]+", ln):
                lines.append(ln)
        if lines:
            out.append(f"[p{i + 1}] " + " | ".join(lines[:14]))
    return "\n".join(out)

def ask(prompt):
    ep = os.environ["AOAI_ENDPOINT"].rstrip("/")
    url = f"{ep}/openai/deployments/{os.environ['AOAI_DEPLOYMENT']}/chat/completions?api-version=2024-10-21"
    body = {"messages": [{"role": "system", "content": "You analyse school textbooks accurately and reply with JSON only."},
                         {"role": "user", "content": prompt}], "max_tokens": 9000, "temperature": 0, "response_format": {"type": "json_object"}}
    for attempt in range(6):
        try:
            req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "api-key": os.environ["AOAI_KEY"]})
            with urllib.request.urlopen(req, timeout=240) as r:
                j = json.loads(r.read())
            return json.loads(j["choices"][0]["message"]["content"]), j.get("usage", {})
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 503):
                time.sleep(15 * (attempt + 1)); continue
            raise
    raise RuntimeError("AI kept failing")

PROMPT = """Kerala SCERT textbook: Class {grade}, {subject} ({medium} medium), book "{book}".
Chapters from its contents page:
{chapters}

Below are the short lines (mostly headings) of each page, as [pN] page number then lines separated by " | ".
For every chapter above:
- "start_page": the page where that chapter begins.
- "headings": every section heading of that chapter, in order, as printed (book's own language and script; do not translate).
- "topics": 4 to 8 MAIN topics chosen from those headings (merge small sub-headings into the bigger section they belong to). Never more than 8.
  For language books (stories, poems, lessons) the topics are the lesson/poem titles inside the unit.
  Leave out activity prompts, figure/flowchart/table labels, questions, exercises, figure captions, "Let us assess", "Extended activities", glossaries and the like.
  If the text is unreadable or missing for a chapter, give 3 to 5 short topic names a teacher would use for it, in the book's language, and set "inferred": true for that chapter.
Reply as JSON: {{"chapters": [{{"number": 1, "start_page": 9, "inferred": false, "headings": ["..."], "topics": ["...", "..."]}}]}}

Pages:
{pages}"""

FILES = "https://samagra.kite.kerala.gov.in/files/samagra-resource/uploads2/tbookscmq"

def ask_vision(images, prompt):
    import base64
    content = [{"type": "text", "text": prompt}] + [{"type": "image_url", "image_url": {"url": "data:image/jpeg;base64," + b, "detail": "high"}} for b in images]
    ep = os.environ["AOAI_ENDPOINT"].rstrip("/")
    url = f"{ep}/openai/deployments/{os.environ['AOAI_DEPLOYMENT']}/chat/completions?api-version=2024-10-21"
    body = {"messages": [{"role": "user", "content": content}], "max_tokens": 3000, "temperature": 0, "response_format": {"type": "json_object"}}
    for attempt in range(6):
        try:
            req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "api-key": os.environ["AOAI_KEY"]})
            with urllib.request.urlopen(req, timeout=240) as r:
                return json.loads(json.loads(r.read())["choices"][0]["message"]["content"])
        except json.JSONDecodeError:
            return {}
        except urllib.error.HTTPError as e:
            if e.code == 400:
                return {}  # e.g. a page the content filter rejects: skip that batch of pages
            if e.code in (429, 500, 503):
                time.sleep(15 * (attempt + 1)); continue
            raise
    raise RuntimeError("AI kept failing")

def vision_outline(b, key):
    """For books whose text layer is missing or unreadable: read the headings off the page images."""
    import base64, io, urllib.parse
    import pypdfium2 as pdfium
    tmp = f"/tmp/t-{key}.pdf"
    urllib.request.urlretrieve(FILES + urllib.parse.quote(b["pdf"]), tmp)
    lines, batches = [], []
    try:
        with LOCK:  # pdfium is not thread-safe
            pdf = pdfium.PdfDocument(tmp)
            n = len(pdf)
            for start in range(0, n, 10):
                imgs = []
                for i in range(start, min(n, start + 10)):
                    img = pdf[i].render(scale=1.0).to_pil().convert("RGB"); img.thumbnail((1000, 1000))
                    buf = io.BytesIO(); img.save(buf, "JPEG", quality=65); imgs.append(base64.b64encode(buf.getvalue()).decode())
                batches.append((start, imgs))
            pdf.close()
    finally:
        os.remove(tmp)
    for start, imgs in batches:
        res = ask_vision(imgs, f"These are pages {start + 1} to {start + len(imgs)} of a school textbook, in order. For each page list the headings printed on it "
                               "(chapter/unit titles, lesson or poem titles, section headings), exactly as printed in the original language and script. "
                               'Reply as JSON: {"pages": [{"page": ' + str(start + 1) + ', "headings": ["..."]}]}')
        for p in res.get("pages", []):
            if p.get("headings"):
                lines.append(f"[p{p.get('page')}] " + " | ".join(p["headings"][:14]))
    return "\n".join(lines)

def process(path):
    key = os.path.basename(path)[:-5]
    dest = os.path.join(OUT, key + ".json")
    try:
        if json.load(open(dest)).get("v") == 3 or (json.load(open(dest)).get("v") == 2 and not malayalam_book(json.load(open(path)))):
            return key, "cached"
    except Exception:
        pass
    b = json.load(open(path))
    chapters = b["toc"].get("chapters", [])
    if not chapters:
        json.dump({"key": key, "chapters": [], "note": "no chapters"}, open(dest, "w"), ensure_ascii=False)
        return key, "no chapters"
    text = b.get("text") or []
    joined = " ".join(text)
    quality = "ok"
    if len(joined.strip()) < 2000:
        quality = "no-text"
    elif malayalam_book(b):
        quality = "garbled"  # the PDFs' Malayalam text layer scrambles conjuncts, so read the pages as images instead
    pages = outline(text) if quality == "ok" else vision_outline(b, key)
    pages = pages[:120000]
    prompt = PROMPT.format(grade=b["grade"], subject=b["subject"], medium="Malayalam" if b["medium"] == "ml" else "English", book=b["book"],
                           chapters="\n".join(f"{c.get('number')}. {c.get('title')}" for c in chapters), pages=pages)
    res, usage = ask(prompt)
    for c in res.get("chapters", []):
        c["topics"] = [t for t in c.get("topics", []) if t][:8]
    json.dump({"v": 3, "key": key, "quality": quality, "chapters": res.get("chapters", []), "usage": usage}, open(dest + ".tmp", "w"), ensure_ascii=False); os.replace(dest + ".tmp", dest)
    n = sum(len(c.get("topics", [])) for c in res.get("chapters", []))
    return key, f"{quality} {len(res.get('chapters', []))} chapters {n} topics"

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    files = sorted(glob.glob(os.path.join(BOOKS, (sys.argv[1] if len(sys.argv) > 1 else "") + "*.json")))
    with cf.ThreadPoolExecutor(4) as ex:
        for fut in cf.as_completed([ex.submit(process, f) for f in files]):
            try:
                print(*fut.result(), flush=True)
            except Exception as e:
                print("ERROR", repr(e)[:200], flush=True)
