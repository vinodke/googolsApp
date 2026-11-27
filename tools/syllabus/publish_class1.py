#!/usr/bin/env python3
"""Publishes the hand-written Class 1 KL-SCERT lessons (both mediums) made with compose_lesson.py:
new lesson text with pictures, the chapter's topics (= the lesson's sections), quiz questions (old ones are archived,
not deleted) and the AI teacher's chunks. The old infographic attachment is unset (its blob is kept for rollback).
Writes a rollback file before changing anything. One transaction.

  bash run_db.sh publish_class1.py ~/w/out             # dry run: prints the mapping, stages ~/w/upload for Blob
  bash ~/w/upload_pics.sh                              # copies ~/w/upload to Blob lesson-files (lessons/{id}/...)
  bash run_db.sh publish_class1.py ~/w/out --apply

~/w/out/<key>/ holds pic-0..5.jpg, poster.jpg, body.md from compose_lesson.py; ~/w/out/lessons/<key>.json the lessons.
key: ml-MAL-NN for Malayalam (both mediums), en-ENG-NN for English (both), <medium>-MATHS-NN, <medium>-ICT-NN.
"""
import datetime, json, os, random, shutil, sys, uuid, zlib
from publish_drafts import chunk
from build_syllabus import connect

OUT = os.path.expanduser(sys.argv[1])
APPLY = "--apply" in sys.argv
UPLOAD = os.path.expanduser("~/w/upload")


def key_for(medium, code, seq):
    if code == "MAL":
        return f"ml-MAL-{seq:02d}"
    if code == "ENG":
        return f"en-ENG-{seq:02d}"
    return f"{medium}-{code}-{seq:02d}"


def main():
    now = datetime.datetime.utcnow()
    cn = connect()
    cur = cn.cursor()
    cur.execute("""SELECT s.Id AS SubjectId, s.Code, s.MediumLanguage AS Medium, h.Id AS ChapterId, h.Title AS Chapter, h.Sequence
                   FROM curriculum.Subjects s JOIN curriculum.AcademicClasses k ON k.Id = s.ClassId
                   JOIN curriculum.Curricula c ON c.Id = k.CurriculumId JOIN curriculum.CurriculumBoards b ON b.Id = c.BoardId
                   JOIN curriculum.Chapters h ON h.SubjectId = s.Id
                   WHERE b.Code = 'KL-SCERT' AND k.GradeNumber = 1 AND s.Code IN ('MAL','ENG','MATHS','ICT')
                   ORDER BY s.MediumLanguage, s.Sequence, h.Sequence""")
    chapters = cur.fetchall()
    plan, missing = [], []
    for c in chapters:
        key = key_for(c["Medium"], c["Code"], c["Sequence"])
        jf, od = os.path.join(OUT, "lessons", key + ".json"), os.path.join(OUT, key)
        if not (os.path.exists(jf) and os.path.exists(os.path.join(od, "body.md"))):
            missing.append(key); continue
        cur.execute("SELECT Id, Title, Body, Language, Status, Kind, EstimatedMinutes, AttachmentBlobName, AttachmentFileName, "
                    "AttachmentContentType, AttachmentBytes FROM content.Lessons WHERE ChapterId = %s AND ArchivedAtUtc IS NULL "
                    "ORDER BY Sequence", (c["ChapterId"],))
        ls = cur.fetchall()
        if len(ls) != 1:
            print("SKIP", key, c["Chapter"], "has", len(ls), "lessons"); continue
        L = json.load(open(jf))
        cur.execute("SELECT Id, Title, Sequence FROM curriculum.Topics WHERE ChapterId = %s ORDER BY Sequence", (c["ChapterId"],))
        topics = cur.fetchall()
        cur.execute("SELECT Id FROM assessment.Questions WHERE ChapterId = %s AND ArchivedAtUtc IS NULL", (c["ChapterId"],))
        qs = [str(r["Id"]) for r in cur.fetchall()]
        plan.append(dict(c=c, key=key, L=L, lesson=ls[0], topics=topics, old_q=qs, body=open(os.path.join(od, "body.md")).read()))
        flag = "" if c["Chapter"].strip() == L["title"].strip() else f"   (chapter title in DB: {c['Chapter']})"
        print(f"{c['Medium']} {c['Code']:5} {c['Sequence']:>2} {key:12} {L['title']}{flag}  topics {len(topics)}->{len(L['sections'])}  "
              f"questions {len(qs)}->{len(L['questions'])}")
    print(len(plan), "lessons planned;", "missing:", missing or "none")

    # stage the pictures for Blob: lessons/{lessonId}/pic-N.jpg, poster.jpg
    shutil.rmtree(UPLOAD, ignore_errors=True)
    for p in plan:
        d = os.path.join(UPLOAD, "lessons", str(p["lesson"]["Id"]).lower())
        os.makedirs(d)
        for f in os.listdir(os.path.join(OUT, p["key"])):
            if f.endswith(".jpg"):
                shutil.copy(os.path.join(OUT, p["key"], f), d)
    print("staged", UPLOAD)

    def js(v):
        return v.isoformat() if isinstance(v, datetime.datetime) else str(v) if isinstance(v, uuid.UUID) else v

    back = os.path.expanduser("~/class1_publish_rollback_%s.json" % now.strftime("%Y%m%d%H%M%S"))
    json.dump([{"key": p["key"], "lesson": {k: js(v) for k, v in p["lesson"].items()},
                "topics": [{k: js(v) for k, v in t.items()} for t in p["topics"]], "archived_questions": p["old_q"]} for p in plan],
              open(back, "w"), ensure_ascii=False)
    print("rollback file", back)
    if not APPLY:
        print("dry run - nothing changed"); return

    new_q = n_chunks = 0
    for p in plan:
        c, L, les = p["c"], p["L"], p["lesson"]
        lid = str(les["Id"])
        cur.execute("UPDATE content.Lessons SET Body = %s, Language = %s, EstimatedMinutes = 15, Status = 3, AttachmentBlobName = NULL, "
                    "AttachmentFileName = NULL, AttachmentContentType = NULL, AttachmentBytes = NULL, UpdatedAtUtc = %s WHERE Id = %s",
                    (p["body"], L["lang"], now, lid))
        for k, s in enumerate(L["sections"]):
            if k < len(p["topics"]):
                cur.execute("UPDATE curriculum.Topics SET Title = %s, Sequence = %s, UpdatedAtUtc = %s WHERE Id = %s",
                            (s["heading"][:300], k + 1, now, p["topics"][k]["Id"]))
            else:
                cur.execute("INSERT INTO curriculum.Topics (Id, ChapterId, Title, Sequence, CreatedAtUtc) VALUES (%s, %s, %s, %s, %s)",
                            (str(uuid.uuid4()), c["ChapterId"], s["heading"][:300], k + 1, now))
        for t in p["topics"][len(L["sections"]):]:   # surplus old topics: removed unless something hangs off them
            cur.execute("SELECT COUNT(*) AS n FROM curriculum.LearningObjectives WHERE TopicId = %s", (t["Id"],))
            if cur.fetchone()["n"] == 0:
                cur.execute("DELETE FROM curriculum.Topics WHERE Id = %s", (t["Id"],))
            else:
                cur.execute("UPDATE curriculum.Topics SET Sequence = %s, UpdatedAtUtc = %s WHERE Id = %s", (100 + t["Sequence"], now, t["Id"]))
        cur.execute("UPDATE assessment.Questions SET ArchivedAtUtc = %s, Status = 4, UpdatedAtUtc = %s WHERE ChapterId = %s AND ArchivedAtUtc IS NULL",
                    (now, now, c["ChapterId"]))
        for q in L["questions"]:
            opts = list(q["options"])   # written with the answer first; mix them so the answer is not always A
            right = opts[q["correct_index"]]
            random.Random(zlib.crc32((p["key"] + q["prompt"]).encode())).shuffle(opts)
            cur.execute("INSERT INTO assessment.Questions (Id, ChapterId, Prompt, OptionsJson, CorrectIndex, Explanation, Difficulty, Language, "
                        "Source, Status, CreatedAtUtc) VALUES (%s, %s, %s, %s, %s, %s, 1, %s, 1, 3, %s)",
                        (str(uuid.uuid4()), c["ChapterId"], q["prompt"][:1000], json.dumps(opts, ensure_ascii=False),
                         opts.index(right), (q.get("explanation") or "")[:1000], L["lang"], now))
            new_q += 1
        cur.execute("DELETE FROM content.ContentChunks WHERE LessonId = %s", (lid,))
        text = "\n".join(line for line in p["body"].split("\n") if not line.startswith("!["))
        for k, t in enumerate(chunk(text)):
            cur.execute("INSERT INTO content.ContentChunks (Id, LessonId, ChapterId, SubjectId, Sequence, Text, Title, Kind, CreatedAtUtc) "
                        "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)",
                        (str(uuid.uuid4()), lid, c["ChapterId"], c["SubjectId"], k, t[:2000], L["title"][:300], les["Kind"], now))
            n_chunks += 1
    cn.commit()
    print(f"APPLIED {len(plan)} lessons, {new_q} questions, {n_chunks} chunks; rollback file {back}")


if __name__ == "__main__":
    main()
