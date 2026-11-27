#!/usr/bin/env python3
"""Stage 3: turns the extracted textbooks (~/syllabus/books + ~/syllabus/topics) into the Googols syllabus.

  python3 build_syllabus.py            # builds ~/syllabus/syllabus.json and kerala_scert_1-10.csv, prints a summary
  CS=... python3 build_syllabus.py --apply   # writes it to the database (one transaction)

Design: board KL-SCERT has two curricula for 2026-27, one per medium (students pick one when they sign up):
  "Kerala State Syllabus · Malayalam medium" (the existing curriculum, renamed) and "Kerala State Syllabus · English medium".
Chapters and topics are published. Re-running is safe: things are matched by grade / subject code / chapter number,
titles are updated, and topics are only added to chapters that have none.
"""
import csv, datetime, glob, json, os, re, sys, uuid

HOME = os.path.expanduser("~/syllabus")
YEAR = "2026-27"
CURRICULUM_NAME = {"ml": "Kerala State Syllabus · Malayalam medium", "en": "Kerala State Syllabus · English medium"}

# subject on Samagra -> (code, English name, Malayalam name, order)
SUBJECTS = {
    "Malayalam": ("MAL", "Malayalam", "മലയാളം", 1),
    "Malayalam (BT)": ("MAL-BT", "Malayalam (Basic Text)", "മലയാളം (അടിസ്ഥാന പാഠാവലി)", 2),
    "English": ("ENG", "English", "ഇംഗ്ലീഷ്", 3),
    "Hindi": ("HIN", "Hindi", "ഹിന്ദി", 4),
    "Maths": ("MATHS", "Mathematics", "ഗണിതം", 5),
    "Mathematics": ("MATHS", "Mathematics", "ഗണിതം", 5),
    "Environmental Science": ("EVS", "Environmental Science", "പരിസരപഠനം", 6),
    "Basic Science": ("BSC", "Basic Science", "അടിസ്ഥാനശാസ്ത്രം", 7),
    "Science": ("SCI", "Science", "ശാസ്ത്രം", 7),
    "Physics": ("PHY", "Physics", "ഭൗതികശാസ്ത്രം", 8),
    "Chemistry": ("CHE", "Chemistry", "രസതന്ത്രം", 9),
    "Biology": ("BIO", "Biology", "ജീവശാസ്ത്രം", 10),
    "Social Science": ("SS", "Social Science", "സാമൂഹ്യശാസ്ത്രം", 11),
    "Social Science 1": ("SS1", "Social Science I", "സാമൂഹ്യശാസ്ത്രം I", 11),
    "Social Science 2": ("SS2", "Social Science II", "സാമൂഹ്യശാസ്ത്രം II", 12),
    "ICT": ("ICT", "ICT", "വിവരവിനിമയ സാങ്കേതികവിദ്യ", 13),
}
SECOND_VOLUME = re.compile(r"(vol(ume)?[\s_\-.]*(ii|2)\b|part[\s_\-.]*(ii|2)\b|[\s_\-]2\s*$|-\s*2\b|_volii|volii)", re.I)
JUNK = re.compile(r"^(contents|ഉള്ളടക്കം|preface|national anthem|pledge|appendix|glossary)$", re.I)


def norm(s):
    return re.sub(r"[\W_]+", "", (s or "").lower())


def clean(s, n=300):
    s = re.sub(r"\s+", " ", str(s or "")).strip(" .:-–•")
    return s[:n]


def build():
    books = [json.load(open(p)) for p in sorted(glob.glob(f"{HOME}/books/*.json"))]
    topics = {}
    for p in glob.glob(f"{HOME}/topics/*.json"):
        t = json.load(open(p))
        topics[t["key"]] = {c.get("number"): c for c in t.get("chapters", [])}
    groups = {}
    for b in books:
        if b["subject"] not in SUBJECTS:
            continue
        key = f"{b['medium']}-{b['grade']:02d}-{re.sub(r'[^A-Za-z0-9]+', '_', b['subject'])}-{b['id']}"
        b["_key"] = key
        groups.setdefault((b["medium"], b["grade"], SUBJECTS[b["subject"]][0]), []).append(b)

    out, skipped = [], []
    for (medium, grade, code), vols in sorted(groups.items()):
        vols.sort(key=lambda b: (1 if SECOND_VOLUME.search(b["book"]) else 0, b["id"]))
        subject = SUBJECTS[vols[0]["subject"]]
        chapters, seen = [], set()
        for b in vols:
            tmap = topics.get(b["_key"], {})
            for c in b["toc"].get("chapters", []):
                title = clean(c.get("title"))
                if not title or JUNK.match(title) or norm(title) in seen:
                    continue
                seen.add(norm(title))
                tps = []
                for t in (tmap.get(c.get("number"), {}).get("topics") or c.get("topics") or []):
                    t = clean(t)
                    if t and norm(t) != norm(title) and norm(t) not in {norm(x) for x in tps}:
                        tps.append(t)
                chapters.append({"no": len(chapters) + 1, "title": title, "topics": tps[:8],
                                 "book": b["book"], "book_id": b["id"], "pdf": b["pdf"],
                                 "start_page": tmap.get(c.get("number"), {}).get("start_page"),
                                 "inferred": bool(tmap.get(c.get("number"), {}).get("inferred"))})
        if not chapters:
            skipped.append(f"{medium} class {grade} {code}")
            continue
        name = subject[1] if medium == "en" or code in ("ENG", "HIN") else f"{subject[1]} · {subject[2]}"
        out.append({"medium": medium, "grade": grade, "code": code, "name": name[:200], "order": subject[3], "chapters": chapters})

    json.dump(out, open(f"{HOME}/syllabus.json", "w"), ensure_ascii=False, indent=1)
    with open(f"{HOME}/kerala_scert_1-10.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["medium", "grade", "subject_code", "subject_name", "chapter_no", "chapter_title", "topics", "textbook"])
        for s in out:
            for c in s["chapters"]:
                w.writerow([s["medium"], s["grade"], s["code"], s["name"], c["no"], c["title"], " | ".join(c["topics"]), c["book"]])
    for m in ("ml", "en"):
        subs = [s for s in out if s["medium"] == m]
        print(f"{m}: {len(subs)} subjects, {sum(len(s['chapters']) for s in subs)} chapters, "
              f"{sum(len(c['topics']) for s in subs for c in s['chapters'])} topics, "
              f"{sum(1 for s in subs for c in s['chapters'] if not c['topics'])} chapters without topics")
        for g in range(1, 11):
            print(f"  class {g}: " + ", ".join(f"{s['code']}({len(s['chapters'])})" for s in subs if s["grade"] == g))
    if skipped:
        print("No chapters found for:", "; ".join(skipped))
    return out


def connect():
    import pymssql
    parts = dict(p.split("=", 1) for p in os.environ["CS"].split(";") if "=" in p)
    n = {k.strip().lower(): v.strip() for k, v in parts.items()}
    server = (n.get("server") or n.get("data source")).replace("tcp:", "").split(",")[0]
    return pymssql.connect(server=server, user=n.get("user id") or n.get("uid"), password=n.get("password") or n.get("pwd"),
                           database=n.get("initial catalog") or n.get("database"), as_dict=True, autocommit=False)


def apply(data):
    now = datetime.datetime.utcnow()
    cn = connect()
    cur = cn.cursor()
    stats = dict(curricula=0, classes=0, subjects=0, chapters=0, retitled=0, published=0, topics=0)
    cur.execute("SELECT Id FROM curriculum.CurriculumBoards WHERE Code = 'KL-SCERT'")
    board = cur.fetchone()
    if not board:
        board_id = str(uuid.uuid4())
        cur.execute("INSERT INTO curriculum.CurriculumBoards (Id, Name, Code, CountryCode, IsEntranceExam, CreatedAtUtc) VALUES (%s, %s, 'KL-SCERT', 'IN', 0, %s)",
                    (board_id, "Kerala State (SCERT)", now))
    else:
        board_id = board["Id"]
    cur.execute("SELECT Id, Name FROM curriculum.Curricula WHERE BoardId = %s AND AcademicYear = %s AND ArchivedAtUtc IS NULL ORDER BY CreatedAtUtc", (board_id, YEAR))
    existing = cur.fetchall()
    curr = {}
    for medium in ("ml", "en"):
        match = next((c for c in existing if c["Name"] == CURRICULUM_NAME[medium]), None)
        if match is None and medium == "ml":
            match = next((c for c in existing if "medium" not in c["Name"].lower()), None)  # the original curriculum
        if match is None:
            cid = str(uuid.uuid4())
            # (board, year, version) is unique, so the second medium gets the next version number
            cur.execute("SELECT ISNULL(MAX(Version), 0) + 1 AS v FROM curriculum.Curricula WHERE BoardId = %s AND AcademicYear = %s", (board_id, YEAR))
            version = cur.fetchone()["v"]
            cur.execute("INSERT INTO curriculum.Curricula (Id, BoardId, Name, AcademicYear, Version, Status, CreatedAtUtc) VALUES (%s, %s, %s, %s, %s, 3, %s)",
                        (cid, board_id, CURRICULUM_NAME[medium], YEAR, version, now))
            stats["curricula"] += 1
        else:
            cid = match["Id"]
            cur.execute("UPDATE curriculum.Curricula SET Name = %s, Status = 3, UpdatedAtUtc = %s WHERE Id = %s", (CURRICULUM_NAME[medium], now, cid))
        curr[medium] = cid

    for s in data:
        cid = curr[s["medium"]]
        cur.execute("SELECT Id FROM curriculum.AcademicClasses WHERE CurriculumId = %s AND GradeNumber = %s", (cid, s["grade"]))
        row = cur.fetchone()
        if row:
            class_id = row["Id"]
        else:
            class_id = str(uuid.uuid4())
            cur.execute("INSERT INTO curriculum.AcademicClasses (Id, CurriculumId, GradeNumber, GradeName, CreatedAtUtc) VALUES (%s, %s, %s, %s, %s)",
                        (class_id, cid, s["grade"], f"Class {s['grade']}", now))
            stats["classes"] += 1
        cur.execute("SELECT Id FROM curriculum.Subjects WHERE ClassId = %s AND Code = %s AND MediumLanguage = %s", (class_id, s["code"], s["medium"]))
        row = cur.fetchone()
        if row:
            subject_id = row["Id"]
            cur.execute("UPDATE curriculum.Subjects SET Name = %s, Sequence = %s, UpdatedAtUtc = %s WHERE Id = %s", (s["name"], s["order"], now, subject_id))
        else:
            subject_id = str(uuid.uuid4())
            cur.execute("INSERT INTO curriculum.Subjects (Id, ClassId, Name, Code, MediumLanguage, Sequence, CreatedAtUtc) VALUES (%s, %s, %s, %s, %s, %s, %s)",
                        (subject_id, class_id, s["name"], s["code"], s["medium"], s["order"], now))
            stats["subjects"] += 1
        cur.execute("SELECT Id, Title, Sequence, Status FROM curriculum.Chapters WHERE SubjectId = %s", (subject_id,))
        have = cur.fetchall()
        by_title = {norm(h["Title"]): h for h in have}
        by_seq = {h["Sequence"]: h for h in have}
        used = set()
        for c in s["chapters"]:
            h = by_title.get(norm(c["title"]))
            if h is None:
                h = by_seq.get(c["no"])
                if h is not None and (h["Id"] in used or norm(h["Title"]) in {norm(x["title"]) for x in s["chapters"]}):
                    h = None
            if h is None:
                chap_id = str(uuid.uuid4())
                cur.execute("INSERT INTO curriculum.Chapters (Id, SubjectId, Title, Sequence, Status, CreatedAtUtc) VALUES (%s, %s, %s, %s, 3, %s)",
                            (chap_id, subject_id, c["title"], c["no"], now))
                stats["chapters"] += 1
            else:
                chap_id = h["Id"]
                if h["Title"] != c["title"] or h["Sequence"] != c["no"] or h["Status"] != 3:
                    cur.execute("UPDATE curriculum.Chapters SET Title = %s, Sequence = %s, Status = 3, UpdatedAtUtc = %s WHERE Id = %s",
                                (c["title"], c["no"], now, chap_id))
                    stats["retitled" if h["Title"] != c["title"] else "published"] += 1
            used.add(chap_id)
            c["id"] = str(chap_id)
            cur.execute("SELECT COUNT(*) AS n FROM curriculum.Topics WHERE ChapterId = %s", (chap_id,))
            if cur.fetchone()["n"] == 0:
                for i, t in enumerate(c["topics"], 1):
                    cur.execute("INSERT INTO curriculum.Topics (Id, ChapterId, Title, Sequence, CreatedAtUtc) VALUES (%s, %s, %s, %s, %s)",
                                (str(uuid.uuid4()), chap_id, t, i, now))
                    stats["topics"] += 1
        # chapters in the database that are not in the official book (e.g. hand-made test chapters) go to the end
        extra = [h for h in have if h["Id"] not in used]
        for i, h in enumerate(sorted(extra, key=lambda h: h["Sequence"]), 1):
            cur.execute("UPDATE curriculum.Chapters SET Sequence = %s, UpdatedAtUtc = %s WHERE Id = %s", (100 + i, now, h["Id"]))
            print(f"kept extra chapter at the end: {s['medium']} class {s['grade']} {s['code']} '{h['Title']}'")
    cn.commit()
    cn.close()
    json.dump(data, open(f"{HOME}/syllabus.json", "w"), ensure_ascii=False, indent=1)  # now with chapter ids, for lesson drafting
    print("Applied:", stats)


if __name__ == "__main__":
    data = build()
    if "--apply" in sys.argv:
        apply(data)
