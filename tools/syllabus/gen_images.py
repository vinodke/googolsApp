#!/usr/bin/env python3
"""Background job: draws one teaching infographic per published lesson and attaches it to the lesson.

For each lesson of the chosen class (both mediums) that has no attachment yet:
  1. gpt-4.1-mini turns the lesson text into an infographic plan: title, 4–6 numbered panels with short
     points, an example and a one-line summary, in the lesson's language.
  2. gpt-image-2 draws it (1536x1024, flat educational style, Googols footer); saved as JPEG (~300 KB).
  3. The PNG goes to Blob container `lesson-files` and is set as the lesson's attachment, exactly like an
     upload from Admin → Content.
Re-running skips lessons that already have an attachment, so the job can be stopped and resumed.

  CS=... BLOB_CS=... AOAI_ENDPOINT=... AOAI_KEY=... python3 gen_images.py --grade 1 [--limit 2] [--medium en] [--force]
"""
import argparse, base64, concurrent.futures as cf, datetime, io, json, threading, os, sys, time, urllib.error, urllib.request, uuid

CHAT_DEPLOYMENT = os.environ.get("AOAI_DEPLOYMENT", "gpt-4.1-mini")
IMAGE_DEPLOYMENT = os.environ.get("AOAI_IMAGE_DEPLOYMENT", "gpt-image-2")
CONTAINER = "lesson-files"


def post(url, body, timeout=300):
    for attempt in range(8):
        try:
            req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                         headers={"Content-Type": "application/json", "api-key": os.environ["AOAI_KEY"]})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            detail = e.read()[:300]
            if e.code in (408, 429, 500, 502, 503, 504):
                time.sleep(min(120, 20 * (attempt + 1)))
                continue
            raise RuntimeError(f"HTTP {e.code}: {detail!r}")
        except (urllib.error.URLError, TimeoutError):
            time.sleep(20)
    raise RuntimeError("AI kept failing")


PLAN_PROMPT = """You design one classroom infographic poster for a Kerala SCERT lesson.
Class {grade}, subject {subject}, chapter "{chapter}". Poster language: {lang}.
Audience: {audience}

From the lesson below, make a plan for ONE poster that teaches the main points in order, like a good teacher's chart:
- "title": the chapter title (short).
- "subtitle": one short line saying what the child will learn.
- "panels": {panels} numbered panels in teaching order. Each: "heading" (2–4 words), "points" (1–3 very short points, max 8 words each), "picture" (a simple concrete drawing idea in English, e.g. "three red apples and two green apples").
- "example": one tiny worked example or real-life example (max 15 words), if it fits the subject; else "".
- "summary": one line to remember (max 14 words).
Use only facts from the lesson. All poster text in {lang}; "picture" always in English.
Reply as JSON: {{"title": "", "subtitle": "", "panels": [{{"heading": "", "points": [""], "picture": ""}}], "example": "", "summary": ""}}

Lesson:
{body}"""


def image_prompt(plan, grade, lang):
    lines = [f"Educational infographic poster for Class {grade} students, landscape 3:2.",
             "Style: clean flat vector illustration, friendly rounded shapes, soft pastel background with subtle blobs, "
             "bold dark-navy headline, each numbered panel in its own rounded card with a different accent colour "
             "(blue, green, orange, purple, teal, pink), a cute simple illustration in every card, arrows showing the order, "
             "generous spacing, crisp readable text, no clutter. Like a premium school chart.",
             f"All text on the poster is in {lang}. Write every text string EXACTLY as given below, spelled correctly; add no other words.",
             f'Headline at top: "{plan["title"]}"',
             f'Sub-headline: "{plan.get("subtitle", "")}"']
    for i, p in enumerate(plan["panels"], 1):
        pts = " / ".join(f'"{x}"' for x in p.get("points", []))
        lines.append(f'Card {i}: number badge "{i}", heading "{p["heading"]}", points: {pts}. Illustration: {p.get("picture", "")}.')
    if plan.get("example"):
        lines.append(f'A highlighted example strip: "{plan["example"]}"')
    lines.append(f'Bottom summary bar with a light-bulb icon: "{plan["summary"]}"')
    lines.append('Small plain wordmark "Googols" in dark navy (one colour) in the bottom-right corner. No other logos or watermarks.')
    return "\n".join(lines)


def connect():
    import pymssql
    parts = dict(p.split("=", 1) for p in os.environ["CS"].split(";") if "=" in p)
    n = {k.strip().lower(): v.strip() for k, v in parts.items()}
    server = (n.get("server") or n.get("data source")).replace("tcp:", "").split(",")[0]
    return pymssql.connect(server=server, user=n.get("user id") or n.get("uid"), password=n.get("password") or n.get("pwd"),
                           database=n.get("initial catalog") or n.get("database"), as_dict=True, autocommit=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--grade", type=int, required=True)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--medium", choices=["en", "ml"])
    ap.add_argument("--force", action="store_true", help="replace existing attachments")
    ap.add_argument("--workers", type=int, default=int(os.environ.get("WORKERS", "2")))
    ap.add_argument("--quality", default=os.environ.get("IMAGE_QUALITY", "medium"))
    a = ap.parse_args()

    from azure.storage.blob import BlobServiceClient, ContentSettings
    container = BlobServiceClient.from_connection_string(os.environ["BLOB_CS"]).get_container_client(CONTAINER)
    if not container.exists():
        container.create_container()

    cn = connect()
    cur = cn.cursor()
    cur.execute("""SELECT l.Id, l.Title, l.Body, l.Language, l.AttachmentBlobName, s.Name AS Subject, s.Code, s.MediumLanguage AS Medium, h.Title AS Chapter
                   FROM content.Lessons l JOIN curriculum.Chapters h ON h.Id = l.ChapterId JOIN curriculum.Subjects s ON s.Id = h.SubjectId
                   JOIN curriculum.AcademicClasses k ON k.Id = s.ClassId JOIN curriculum.Curricula c ON c.Id = k.CurriculumId
                   JOIN curriculum.CurriculumBoards b ON b.Id = c.BoardId
                   WHERE b.Code = 'KL-SCERT' AND k.GradeNumber = %s AND l.Status = 3 AND l.ArchivedAtUtc IS NULL AND h.Status = 3
                   ORDER BY s.MediumLanguage, s.Sequence, h.Sequence""", (a.grade,))
    lessons = [l for l in cur.fetchall() if (a.force or not l["AttachmentBlobName"]) and (not a.medium or l["Medium"] == a.medium)]
    if a.limit:
        lessons = lessons[:a.limit]
    print(f"class {a.grade}: {len(lessons)} lessons to illustrate", flush=True)
    ep = os.environ["AOAI_ENDPOINT"].rstrip("/")
    young = a.grade <= 4
    ok = fail = 0
    lock = threading.Lock()

    def work(l):
        nonlocal ok, fail
        try:
            lang = {"ml": "Malayalam", "en": "English"}.get(l["Language"], "English")
            if l["Code"] == "HIN":
                lang = "Hindi"
            plan = post(f"{ep}/openai/deployments/{CHAT_DEPLOYMENT}/chat/completions?api-version=2024-10-21", {
                "messages": [{"role": "user", "content": PLAN_PROMPT.format(
                    grade=a.grade, subject=l["Subject"], chapter=l["Chapter"], lang=lang, body=l["Body"][:9000],
                    panels="4" if young else "5 or 6",
                    audience="young children: very simple words, big friendly pictures, few words" if young else "school students")}],
                "temperature": 0.2, "max_tokens": 1500, "response_format": {"type": "json_object"}})
            plan = json.loads(plan["choices"][0]["message"]["content"])
            plan["panels"] = plan.get("panels", [])[:6]
            img = post(f"{ep}/openai/deployments/{IMAGE_DEPLOYMENT}/images/generations?api-version=2025-04-01-preview", {
                "prompt": image_prompt(plan, a.grade, lang), "size": "1536x1024", "quality": a.quality, "n": 1}, timeout=600)
            png = base64.b64decode(img["data"][0]["b64_json"])
            if not png.startswith(b"\x89PNG"):
                raise RuntimeError("not a PNG")
            # JPEG is ~6x smaller than the PNG, which matters for children on mobile data
            from PIL import Image
            buf = io.BytesIO()
            Image.open(io.BytesIO(png)).convert("RGB").save(buf, "JPEG", quality=86, optimize=True, progressive=True)
            png = buf.getvalue()
            blob = f"{datetime.datetime.now(datetime.UTC):%Y/%m}/{uuid.uuid4().hex}.jpg"
            container.upload_blob(blob, png, content_settings=ContentSettings(content_type="image/jpeg"))
            old = l["AttachmentBlobName"]
            with lock:
                cur.execute("""UPDATE content.Lessons SET AttachmentBlobName = %s, AttachmentFileName = %s, AttachmentContentType = 'image/jpeg',
                               AttachmentBytes = %s, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = %s""",
                            (blob, "infographic.jpg", len(png), l["Id"]))
            if old:
                container.delete_blob(old, delete_snapshots="include") if container.get_blob_client(old).exists() else None
            with lock:
                ok += 1
            print(f"ok {l['Medium']} {l['Code']} '{l['Chapter']}' -> {blob} ({len(png) // 1024} KB)", flush=True)
        except Exception as e:
            with lock:
                fail += 1
            print(f"ERROR {l['Medium']} {l['Code']} '{l['Chapter']}': {str(e)[:200]}", flush=True)
    with cf.ThreadPoolExecutor(a.workers) as ex:
        list(ex.map(work, lessons))
    print(f"done: {ok} images, {fail} failed", flush=True)


if __name__ == "__main__":
    main()
