#!/usr/bin/env python3
"""Stage 4: AI-drafts one study lesson and 10 practice questions for every chapter, grounded in the
chapter's own textbook pages. Everything is saved as Draft (not visible to students) for a content
manager to review and publish. Chapters that already have a lesson are skipped, so re-running is safe.

  CS=... AOAI_*=... python3 draft_content.py [limit]
"""
import concurrent.futures as cf, datetime, json, os, re, sys, threading, time, urllib.error, urllib.request, uuid

HOME = os.path.expanduser("~/syllabus")
LOCK = threading.Lock()
LANG_NAME = {"ml": "Malayalam", "en": "English"}


def content_language(s):
    if s["code"] == "ENG":
        return "en"
    if s["code"] in ("MAL", "MAL-BT"):
        return "ml"
    return s["medium"]


def ask(prompt):
    ep = os.environ["AOAI_ENDPOINT"].rstrip("/")
    url = f"{ep}/openai/deployments/{os.environ['AOAI_DEPLOYMENT']}/chat/completions?api-version=2024-10-21"
    body = {"messages": [{"role": "system", "content": "You are an experienced Kerala school teacher writing study material for students. Reply with JSON only."},
                         {"role": "user", "content": prompt}], "max_tokens": 9000, "temperature": 0.3, "response_format": {"type": "json_object"}}
    for attempt in range(6):
        try:
            req = urllib.request.Request(url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "api-key": os.environ["AOAI_KEY"]})
            with urllib.request.urlopen(req, timeout=300) as r:
                j = json.loads(r.read())
            return json.loads(j["choices"][0]["message"]["content"])
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 503):
                time.sleep(20 * (attempt + 1)); continue
            raise
        except json.JSONDecodeError:
            if attempt < 2:
                continue
            raise
    raise RuntimeError("AI kept failing")


PROMPT = """Class {grade}, {subject}, Kerala SCERT syllabus ({medium} medium). Chapter {no}: "{title}".
Main topics: {topics}

Write in {lang}{extra}. Use only what this chapter teaches at this class level; the chapter's textbook text is below (it may be partial or messy).

1. "lesson": a clear study lesson a student can read in about 15 minutes. Light markdown only: "## " headings (one per main topic), short paragraphs, "- " lists, simple everyday examples, and a final "## Quick recap" with 4–6 bullet points. No tables, images or links. 500–1100 words.
2. "questions": exactly 10 multiple-choice questions on this chapter: 4 easy, 4 medium, 2 hard. Each has 4 options, exactly one correct, and a one-sentence explanation. No "all of the above" / "none of the above".

Reply as JSON: {{"lesson": "...", "questions": [{{"prompt": "...", "options": ["...", "...", "...", "..."], "correct_index": 0, "difficulty": 1, "explanation": "..."}}]}}

Textbook text:
{text}"""


def chapter_text(book, start, end):
    pages = book.get("text") or []
    if not pages or not start:
        return ""
    chunk = "\n".join(pages[start - 1:(end - 1 if end else start + 24)])
    return re.sub(r"[ \t]+", " ", chunk)[:24000]


def connect():
    import pymssql
    parts = dict(p.split("=", 1) for p in os.environ["CS"].split(";") if "=" in p)
    n = {k.strip().lower(): v.strip() for k, v in parts.items()}
    server = (n.get("server") or n.get("data source")).replace("tcp:", "").split(",")[0]
    return pymssql.connect(server=server, user=n.get("user id") or n.get("uid"), password=n.get("password") or n.get("pwd"),
                           database=n.get("initial catalog") or n.get("database"), as_dict=True, autocommit=False)


def main():
    data = json.load(open(f"{HOME}/syllabus.json"))
    books = {}
    for p in os.listdir(f"{HOME}/books"):
        b = json.load(open(f"{HOME}/books/{p}"))
        books[b["id"]] = b
    cn = connect()
    cur = cn.cursor()
    cur.execute("SELECT DISTINCT ChapterId FROM content.Lessons WHERE ArchivedAtUtc IS NULL")
    done = {str(r["ChapterId"]).lower() for r in cur.fetchall()}

    jobs = []
    for s in data:
        chs = s["chapters"]
        for i, c in enumerate(chs):
            if not c.get("id") or c["id"].lower() in done:
                continue
            nxt = chs[i + 1] if i + 1 < len(chs) and chs[i + 1]["book_id"] == c["book_id"] else None
            jobs.append((s, c, nxt))
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else len(jobs)
    jobs = jobs[:limit]
    print(f"{len(jobs)} chapters to draft", flush=True)

    def work(job):
        s, c, nxt = job
        lang = content_language(s)
        extra = ""
        if s["code"] == "HIN":
            extra = f" (the subject is Hindi: keep Hindi words, sentences and questions in Hindi, explanations in {LANG_NAME[s['medium']]})"
            lang = "Hindi"
        else:
            lang = LANG_NAME[lang]
        book = books.get(c["book_id"], {})
        # Malayalam text taken from these PDFs is scrambled, so Malayalam chapters are drafted from the title and topics only
        malayalam = book.get("subject") in ("Malayalam", "Malayalam (BT)") or (s["medium"] == "ml" and s["code"] not in ("ENG", "HIN"))
        text = ("" if malayalam else chapter_text(book, c.get("start_page"), nxt.get("start_page") if nxt else None)) or "(not available: use the chapter title and topics)"
        res = ask(PROMPT.format(grade=s["grade"], subject=s["name"], medium=LANG_NAME[s["medium"]], no=c["no"], title=c["title"],
                                topics="; ".join(c["topics"]) or "(see text)", lang=lang, extra=extra, text=text))
        lesson = (res.get("lesson") or "").strip()
        qs = [q for q in res.get("questions", []) if isinstance(q.get("options"), list) and 2 <= len(q["options"]) <= 6
              and isinstance(q.get("correct_index"), int) and 0 <= q["correct_index"] < len(q["options"]) and q.get("prompt")]
        if len(lesson) < 200:
            raise RuntimeError(f"short lesson for {c['title']}")
        now = datetime.datetime.utcnow()
        db_lang = content_language(s)
        with LOCK:
            cur.execute("INSERT INTO content.Lessons (Id, ChapterId, Title, Sequence, Body, Language, Kind, EstimatedMinutes, Status, CreatedAtUtc) "
                        "VALUES (%s, %s, %s, 1, %s, %s, 1, 15, 0, %s)", (str(uuid.uuid4()), c["id"], c["title"][:300], lesson, db_lang, now))
            for q in qs[:10]:
                opts = json.dumps([str(o).strip()[:400] for o in q["options"]], ensure_ascii=False)
                if len(opts) > 2500:
                    continue
                cur.execute("INSERT INTO assessment.Questions (Id, ChapterId, Prompt, OptionsJson, CorrectIndex, Explanation, Difficulty, Language, Source, Status, CreatedAtUtc) "
                            "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, 1, 0, %s)",
                            (str(uuid.uuid4()), c["id"], str(q["prompt"])[:1000], opts, q["correct_index"], (q.get("explanation") or "")[:1000] or None,
                             min(3, max(1, int(q.get("difficulty") or 2))), db_lang, now))
            cn.commit()
        return f"{s['medium']} {s['grade']} {s['code']} {c['no']}: {len(lesson)} chars, {len(qs[:10])} questions"

    ok = fail = 0
    with cf.ThreadPoolExecutor(8) as ex:
        for fut in cf.as_completed([ex.submit(work, j) for j in jobs]):
            try:
                print(fut.result(), flush=True); ok += 1
            except Exception as e:
                print("ERROR", repr(e)[:200], flush=True); fail += 1
    cn.close()
    print(f"done: {ok} drafted, {fail} failed")


if __name__ == "__main__":
    main()
