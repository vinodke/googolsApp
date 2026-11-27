#!/usr/bin/env python3
"""Publishes the AI-drafted lessons and questions created on or after a given UTC time (the syllabus
drafting run), and builds the AI teacher's retrieval chunks for each lesson exactly like the app's
LessonIndexer / TextChunker do when a lesson is published from the admin screen.

  CS=... python3 publish_drafts.py 2026-11-27T11:00:00
Only rows that are still Draft and AI-made (lessons Kind=1 from this run, questions Source=AiDraft) are touched.
"""
import datetime, sys, uuid
from build_syllabus import connect

MAX_CHARS = 900


def split_long(para, max_chars):
    """Very long paragraphs are cut at sentence ends where possible (same rule as TextChunker.SplitLong)."""
    while len(para) > max_chars:
        cut = -1
        for mark in (". ", "? ", "! ", "। "):
            i = para.rfind(mark, 0, max_chars)
            cut = max(cut, -1 if i < 0 else i + len(mark))
        if cut < max_chars // 3:
            cut = para.rfind(" ", 0, max_chars)
        if cut <= 0:
            cut = max_chars
        yield para[:cut].strip()
        para = para[cut:].strip()
    if para:
        yield para


def chunk(text, max_chars=MAX_CHARS):
    chunks, current, heading = [], "", None
    if not text or not text.strip():
        return chunks
    paragraphs = [p.strip() for p in text.replace("\r\n", "\n").split("\n\n") if p.strip()]

    def flush():
        nonlocal current
        if current:
            chunks.append(current.strip())
            current = ""

    for para in paragraphs:
        if para.startswith("#"):
            flush()
            heading = para.lstrip("# ")
            continue
        for piece in split_long(para, max_chars):
            if current and len(current) + len(piece) + 2 > max_chars:
                flush()
            if not current and heading is not None:
                current += heading + ": "
            if current and not current.endswith(": "):
                current += "\n\n"
            current += piece
    flush()
    return chunks


def main():
    since = sys.argv[1]
    now = datetime.datetime.now(datetime.UTC).replace(tzinfo=None)
    cn = connect()
    cur = cn.cursor()
    cur.execute("""SELECT l.Id, l.ChapterId, l.Title, l.Sequence, l.Body, l.Kind, h.SubjectId
                   FROM content.Lessons l JOIN curriculum.Chapters h ON h.Id = l.ChapterId
                   WHERE l.Status = 0 AND l.Kind = 1 AND l.ArchivedAtUtc IS NULL AND l.CreatedAtUtc >= %s""", (since,))
    lessons = cur.fetchall()
    n_chunks = 0
    for i, l in enumerate(lessons, 1):
        cur.execute("DELETE FROM content.ContentChunks WHERE LessonId = %s", (l["Id"],))
        for k, text in enumerate(chunk(l["Body"])):
            cur.execute("INSERT INTO content.ContentChunks (Id, LessonId, ChapterId, SubjectId, Sequence, Text, Title, Kind, CreatedAtUtc) "
                        "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s)",
                        (str(uuid.uuid4()), l["Id"], l["ChapterId"], l["SubjectId"], l["Sequence"] * 1000 + k, text[:2000], l["Title"][:300], l["Kind"], now))
            n_chunks += 1
        cur.execute("UPDATE content.Lessons SET Status = 3, UpdatedAtUtc = %s WHERE Id = %s", (now, l["Id"]))
        if i % 100 == 0:
            cn.commit()
            print(f"{i}/{len(lessons)} lessons", flush=True)
    cur.execute("UPDATE assessment.Questions SET Status = 3, UpdatedAtUtc = %s WHERE Status = 0 AND Source = 1 AND ArchivedAtUtc IS NULL AND CreatedAtUtc >= %s", (now, since))
    nq = cur.rowcount
    cn.commit()
    cn.close()
    print(f"published {len(lessons)} lessons ({n_chunks} AI-teacher chunks) and {nq} questions")


if __name__ == "__main__":
    main()
