#!/usr/bin/env python3
"""Read-only export of one class for review: its KL-SCERT syllabus and content from the database, plus page
pictures of the class's official SCERT textbooks (both mediums) from Samagra. Changes nothing anywhere.

  bash run_db.sh export_class.py 1        # -> ~/class1_export.tgz  (download it from Cloud Shell)
"""
import io, json, os, re, subprocess, sys, tarfile, urllib.parse, urllib.request

GRADE = int(sys.argv[1]) if len(sys.argv) > 1 else 1
OUT = os.path.expanduser(f"~/class{GRADE}_export")
API = "https://samagra.kite.kerala.gov.in/v2/api/public/getSubjectTextbooks/{m}/{c}"
FILES = "https://samagra.kite.kerala.gov.in/files/samagra-resource/uploads2/tbookscmq"
CORE = {"Maths", "Mathematics", "Malayalam", "Malayalam (BT)", "English", "Hindi", "ICT", "Environmental Science",
        "Basic Science", "Science", "Social Science", "Social Science 1", "Social Science 2", "Physics", "Chemistry", "Biology"}


def connect():
    import pymssql
    parts = dict(p.split("=", 1) for p in os.environ["CS"].split(";") if "=" in p)
    n = {k.strip().lower(): v.strip() for k, v in parts.items()}
    server = (n.get("server") or n.get("data source")).replace("tcp:", "").split(",")[0]
    return pymssql.connect(server=server, user=n.get("user id") or n.get("uid"), password=n.get("password") or n.get("pwd"),
                           database=n.get("initial catalog") or n.get("database"), as_dict=True)


def rows(cur, sql, *args):
    cur.execute(sql, args)
    return [{k: (str(v) if v is not None and not isinstance(v, (int, float, str)) else v) for k, v in r.items()} for r in cur.fetchall()]


def export_db():
    cur = connect().cursor()
    base = """FROM curriculum.Subjects s JOIN curriculum.AcademicClasses k ON k.Id = s.ClassId
              JOIN curriculum.Curricula c ON c.Id = k.CurriculumId JOIN curriculum.CurriculumBoards b ON b.Id = c.BoardId
              WHERE b.Code = 'KL-SCERT' AND k.GradeNumber = %s"""
    data = {"subjects": rows(cur, f"SELECT s.Id, s.Name, s.Code, s.MediumLanguage, s.Sequence, c.Name AS Curriculum {base} ORDER BY s.MediumLanguage, s.Sequence", GRADE)}
    data["chapters"] = rows(cur, f"SELECT h.Id, h.SubjectId, h.Title, h.Sequence, h.Status FROM curriculum.Chapters h WHERE h.SubjectId IN (SELECT s.Id {base}) ORDER BY h.SubjectId, h.Sequence", GRADE)
    ch = "SELECT h.Id FROM curriculum.Chapters h WHERE h.SubjectId IN (SELECT s.Id " + base + ")"
    data["topics"] = rows(cur, f"SELECT t.Id, t.ChapterId, t.Title, t.Sequence FROM curriculum.Topics t WHERE t.ChapterId IN ({ch}) ORDER BY t.ChapterId, t.Sequence", GRADE)
    data["lessons"] = rows(cur, f"""SELECT l.Id, l.ChapterId, l.Title, l.Sequence, l.Body, l.Language, l.Status, l.EstimatedMinutes,
                                       l.AttachmentBlobName, l.AttachmentFileName, l.ArchivedAtUtc
                                FROM content.Lessons l WHERE l.ChapterId IN ({ch}) ORDER BY l.ChapterId, l.Sequence""", GRADE)
    data["questions"] = rows(cur, f"""SELECT q.Id, q.ChapterId, q.Prompt, q.OptionsJson, q.CorrectIndex, q.Explanation, q.Difficulty, q.Language, q.Status
                                  FROM assessment.Questions q WHERE q.ArchivedAtUtc IS NULL AND q.ChapterId IN ({ch})""", GRADE)
    json.dump(data, open(os.path.join(OUT, "db.json"), "w"), ensure_ascii=False, indent=1)
    print({k: len(v) for k, v in data.items()}, flush=True)


def export_books():
    import pypdfium2 as pdfium
    books = []
    for m, med in ((1, "ml"), (2, "en")):
        d = json.load(urllib.request.urlopen(urllib.request.Request(API.format(m=m, c=GRADE), headers={"User-Agent": "googols-syllabus"}), timeout=60))["data"]
        names = {s["id"]: s["subjectName"].strip() for s in d["subjectData"]}
        for t in d["textbookData"]:
            if names.get(t["subjectId"], "?") in CORE:
                books.append({"medium": med, "subject": names[t["subjectId"]], "book": t["chapterName"].strip(), "id": t["id"], "pdf": t["chapterPdfUrl"]})
    seen = set()
    for b in books:
        key = f"{b['medium']}-{re.sub(r'[^A-Za-z0-9]+', '_', b['subject'])}-{b['id']}"
        if b["pdf"] in seen:
            b["same_as_other_medium"] = True
            continue
        seen.add(b["pdf"])
        tmp = f"/tmp/{key}.pdf"
        try:
            urllib.request.urlretrieve(FILES + urllib.parse.quote(b["pdf"]), tmp)
            pdf = pdfium.PdfDocument(tmp)
            os.makedirs(os.path.join(OUT, "books", key), exist_ok=True)
            for i in range(len(pdf)):
                img = pdf[i].render(scale=1.4).to_pil().convert("RGB")
                img.thumbnail((1000, 1000))
                img.save(os.path.join(OUT, "books", key, f"p{i + 1:03d}.jpg"), "JPEG", quality=55, optimize=True)
            b["pages"], b["folder"] = len(pdf), key
            pdf.close()
            print("book", key, b["pages"], "pages", flush=True)
        except Exception as e:
            b["error"] = str(e)[:200]
            print("ERROR", key, b["error"], flush=True)
        finally:
            if os.path.exists(tmp):
                os.remove(tmp)
    json.dump(books, open(os.path.join(OUT, "books.json"), "w"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    subprocess.run([sys.executable, "-m", "pip", "install", "--user", "-q", "pypdfium2", "pillow"], check=False)
    os.makedirs(OUT, exist_ok=True)
    export_db()
    export_books()
    tgz = OUT + ".tgz"
    with tarfile.open(tgz, "w:gz") as t:
        t.add(OUT, arcname=os.path.basename(OUT))
    print(f"DONE -> {tgz} ({os.path.getsize(tgz) // 1024 // 1024} MB). Download it with Cloud Shell's Manage files > Download.", flush=True)
