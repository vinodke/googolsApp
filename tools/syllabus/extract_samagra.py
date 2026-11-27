#!/usr/bin/env python3
"""Builds the Kerala SCERT class 1–10 chapter list from the official Samagra textbook PDFs.

For each core textbook (Malayalam and English medium) it downloads the PDF, renders the first pages,
asks the AI (vision) to read the table of contents, and also saves the book's plain text for drafting
lessons later. PDFs are deleted after use (Cloud Shell has 5 GB). Results: ~/syllabus/books/*.json.
Needs env AOAI_ENDPOINT, AOAI_DEPLOYMENT, AOAI_KEY (read from the app's settings by run_extract.sh).
"""
import base64, concurrent.futures as cf, io, json, os, re, sys, time, urllib.parse, urllib.request

API = "https://samagra.kite.kerala.gov.in/v2/api/public/getSubjectTextbooks/{m}/{c}"
FILES = "https://samagra.kite.kerala.gov.in/files/samagra-resource/uploads2/tbookscmq"
OUT = os.path.expanduser("~/syllabus/books")
MEDIUMS = {1: "ml", 2: "en"}
# Core academic subjects only (optional languages, art, PE and work education are left out on purpose).
CORE = {"Maths", "Mathematics", "Malayalam", "Malayalam (BT)", "English", "Hindi", "ICT", "Environmental Science", "Basic Science",
        "Science", "Social Science", "Social Science 1", "Social Science 2", "Physics", "Chemistry", "Biology"}

def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "googols-syllabus"}), timeout=60) as r:
        return json.loads(r.read())

def books():
    for m, med in MEDIUMS.items():
        for c in range(1, 11):
            d = get_json(API.format(m=m, c=c))["data"]
            names = {s["id"]: s["subjectName"].strip() for s in d["subjectData"]}
            for t in d["textbookData"]:
                subject = names.get(t["subjectId"], "?")
                if subject in CORE:
                    yield {"medium": med, "grade": c, "subject": subject, "book": t["chapterName"].strip(), "id": t["id"], "pdf": t["chapterPdfUrl"]}

def ask_vision(images, prompt):
    ep = os.environ["AOAI_ENDPOINT"].rstrip("/")
    url = f"{ep}/openai/deployments/{os.environ['AOAI_DEPLOYMENT']}/chat/completions?api-version=2024-10-21"
    content = [{"type": "text", "text": prompt}] + [{"type": "image_url", "image_url": {"url": "data:image/jpeg;base64," + b, "detail": "high"}} for b in images]
    body = {"messages": [{"role": "system", "content": "You read school textbook pages accurately and reply with JSON only."},
                         {"role": "user", "content": content}], "max_tokens": 2500, "temperature": 0, "response_format": {"type": "json_object"}}
    for attempt in range(5):
        try:
            req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "api-key": os.environ["AOAI_KEY"]})
            with urllib.request.urlopen(req, timeout=180) as r:
                j = json.loads(r.read())
            return json.loads(j["choices"][0]["message"]["content"]), j.get("usage", {})
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 503):
                time.sleep(10 * (attempt + 1)); continue
            raise
    raise RuntimeError("AI kept failing")

PROMPT = """These are the first pages of a Kerala SCERT school textbook: {desc}.
Find the table of contents (it may be titled Contents, ഉള്ളടക്കം, विषय-सूची, Units, or similar) and list the chapters/units in order.
Copy each title exactly as printed, in the book's own language and script. Do not translate. Leave out the preface, constitution, pledge, national anthem and appendices.
If a chapter has numbered sub-sections listed in the contents, put them in "topics".
If there is no contents page, return the unit titles you can see on these pages, and set "found_contents" to false.
Reply as JSON: {{"found_contents": true, "chapters": [{{"number": 1, "title": "...", "topics": ["..."]}}]}}"""

def process(b):
    key = f"{b['medium']}-{b['grade']:02d}-{re.sub(r'[^A-Za-z0-9]+', '_', b['subject'])}-{b['id']}"
    path = os.path.join(OUT, key + ".json")
    if os.path.exists(path):
        return key, "cached"
    import pypdfium2 as pdfium
    from pypdf import PdfReader
    url = FILES + urllib.parse.quote(b["pdf"])
    tmp = f"/tmp/{key}.pdf"
    urllib.request.urlretrieve(url, tmp)
    try:
        pdf = pdfium.PdfDocument(tmp)
        pages = len(pdf)
        images = []
        for i in range(min(10, pages)):
            img = pdf[i].render(scale=1.25).to_pil().convert("RGB")
            img.thumbnail((1400, 1400))
            buf = io.BytesIO(); img.save(buf, "JPEG", quality=70)
            images.append(base64.b64encode(buf.getvalue()).decode())
        pdf.close()
        desc = f"Class {b['grade']}, {b['subject']} ({'Malayalam' if b['medium'] == 'ml' else 'English'} medium), book '{b['book']}'"
        toc, usage = ask_vision(images, PROMPT.format(desc=desc))
        try:
            text = [p.extract_text() or "" for p in PdfReader(tmp).pages]
        except Exception as ex:
            text = []
        json.dump({**b, "pages": pages, "toc": toc, "usage": usage, "text": text}, open(path, "w"), ensure_ascii=False)
        return key, f"{len(toc.get('chapters', []))} chapters, contents={toc.get('found_contents')}"
    finally:
        os.remove(tmp)

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    todo = list(books())
    print(f"{len(todo)} textbooks", flush=True)
    only = sys.argv[1] if len(sys.argv) > 1 else None
    if only:
        todo = [b for b in todo if f"{b['medium']}-{b['grade']}" == only]
    with cf.ThreadPoolExecutor(4) as ex:
        for fut in cf.as_completed([ex.submit(process, b) for b in todo]):
            try:
                print(*fut.result(), flush=True)
            except Exception as e:
                print("ERROR", repr(e)[:200], flush=True)
