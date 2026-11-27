#!/usr/bin/env python3
"""One-off clean-up: removes today's AI drafts for Malayalam-language chapters that were drafted from
scrambled PDF text / topics, so draft_content.py can redraft them. Only Draft, AI-made rows created on
or after the given UTC time are touched (never published or hand-made content)."""
import json, os, sys
from build_syllabus import connect, HOME
since = sys.argv[1]  # e.g. 2026-11-27T10:00:00
data = json.load(open(f"{HOME}/syllabus.json"))
books = {}
for p in os.listdir(f"{HOME}/books"):
    b = json.load(open(f"{HOME}/books/{p}")); books[b["id"]] = b
ids = [c["id"] for s in data for c in s["chapters"] if c.get("id") and (
    books.get(c["book_id"], {}).get("subject") in ("Malayalam", "Malayalam (BT)") or (s["medium"] == "ml" and s["code"] not in ("ENG", "HIN")))]
cn = connect(); cur = cn.cursor(); nl = nq = 0
for i in range(0, len(ids), 200):
    part = ids[i:i + 200]; ph = ",".join(["%s"] * len(part))
    cur.execute(f"DELETE FROM content.Lessons WHERE Status = 0 AND Kind = 1 AND CreatedAtUtc >= %s AND ChapterId IN ({ph})", (since, *part)); nl += cur.rowcount
    cur.execute(f"DELETE FROM assessment.Questions WHERE Status = 0 AND Source = 1 AND CreatedAtUtc >= %s AND ChapterId IN ({ph})", (since, *part)); nq += cur.rowcount
cn.commit(); print(f"removed {nl} draft lessons and {nq} draft questions from {len(ids)} Malayalam chapters")
