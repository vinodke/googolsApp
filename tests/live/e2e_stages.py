#!/usr/bin/env python3
"""Live end-to-end check of the AI teacher upgrades, tests & progress, and schools/homework/reports.

Runs against the deployed app with throwaway qa-*@googols.test accounts and a QA-only syllabus,
then deletes everything it created. Needs env CS (the SQL connection string) for role grants and
clean-up; run it through tests/live/run.sh, which opens and closes a temporary firewall rule.
Nothing here touches real content: the QA board is separate from every real syllabus.
"""
import base64, http.cookiejar, json, os, secrets, sys, time, urllib.error, urllib.request

BASE = os.environ.get("BASE", "https://googols-app.azurewebsites.net")
RUN = secrets.token_hex(3)
PW = "Qa!" + secrets.token_urlsafe(12)
START = time.strftime("%Y-%m-%d %H:%M:%S", time.gmtime())
results = []

def check(name, ok, detail=""):
    results.append((name, bool(ok)))
    print(("PASS " if ok else "FAIL ") + name + (f"  [{detail}]" if detail and not ok else ""), flush=True)
    return ok

class Client:
    def __init__(self, label):
        self.label = label
        self.email = f"qa-{label}-{RUN}@googols.test"
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))

    def call(self, method, path, body=None, raw=False, accept=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(BASE + "/api/v1" + path, data=data, method=method)
        if data is not None:
            req.add_header("Content-Type", "application/json")
        if accept:
            req.add_header("Accept", accept)
        try:
            r = self.op.open(req, timeout=120)
            status, payload, headers = r.status, r.read(), r.headers
        except urllib.error.HTTPError as e:
            status, payload, headers = e.code, e.read(), e.headers
        if raw:
            return status, payload, headers
        try:
            return status, json.loads(payload) if payload else None
        except ValueError:
            return status, payload.decode("utf-8", "replace")

    def sse(self, path, body):
        status, payload, headers = self.call("POST", path, body, raw=True, accept="text/event-stream")
        events = []
        if status == 200 and "text/event-stream" in (headers.get("Content-Type") or ""):
            for block in payload.decode().split("\n\n"):
                ev, data = "message", []
                for line in block.split("\n"):
                    if line.startswith("event:"): ev = line[6:].strip()
                    elif line.startswith("data:"): data.append(line[5:].strip())
                if data: events.append((ev, json.loads("\n".join(data))))
        return status, events

def auth_pause():
    time.sleep(7)  # auth endpoints allow 10 requests a minute per IP

def register(c):
    s, _ = c.call("POST", "/auth/register", {"email": c.email, "password": PW}); auth_pause()
    return s

def login(c):
    s, _ = c.call("POST", "/auth/login?useCookies=true", {"email": c.email, "password": PW}); auth_pause()
    return s

# ---- SQL helpers (role grants and clean-up)
def sql():
    import pymssql
    parts = dict(p.split("=", 1) for p in os.environ["CS"].split(";") if "=" in p)
    norm = {k.strip().lower(): v.strip() for k, v in parts.items()}
    server = norm.get("server") or norm.get("data source")
    server = server.replace("tcp:", "").split(",")[0]
    return pymssql.connect(server=server, user=norm.get("user id") or norm.get("uid"), password=norm.get("password") or norm.get("pwd"),
                           database=norm.get("initial catalog") or norm.get("database"), as_dict=True)

def grant_role(email, role):
    with sql() as cn, cn.cursor() as cur:
        cur.execute("INSERT INTO [identity].[UserRoles](UserId, RoleId) SELECT u.Id, r.Id FROM [identity].[Users] u, [identity].[Roles] r "
                    "WHERE u.NormalizedEmail = %s AND r.Name = %s", (email.upper(), role))
        cn.commit()

CLEANUP = [
    # AI logs, eval data, report logs written by QA accounts
    "DELETE FROM ai.UsageLogs WHERE UserId IN (SELECT Id FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test') OR StudentId IN (SELECT Id FROM learning.StudentProfiles WHERE UserId IN (SELECT Id FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test'))",
    "DELETE FROM ai.EvalRuns WHERE CreatedAtUtc >= %(start)s AND ResultsJson LIKE '%%QA-E2E%%'",
    "DELETE FROM ai.EvalCases WHERE SubjectId IN (SELECT s.Id FROM curriculum.Subjects s JOIN curriculum.AcademicClasses k ON s.ClassId = k.Id JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM learning.ParentReportLogs WHERE ParentUserId IN (SELECT Id FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test')",
    # Students (cascades: enrolments, progress, attempts, written answers, mastery, bookmarks, chats, XP, memberships, submissions)
    "DELETE FROM learning.StudentProfiles WHERE UserId IN (SELECT Id FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test')",
    # Teacher classrooms (assignments cascade), schools
    "DELETE FROM school.Classrooms WHERE TeacherUserId IN (SELECT Id FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test')",
    "DELETE FROM school.Divisions WHERE SchoolId IN (SELECT Id FROM school.Schools WHERE Code LIKE 'QAE2E%%')",
    "DELETE FROM school.Schools WHERE Code LIKE 'QAE2E%%'",
    # QA syllabus
    "DELETE FROM assessment.Exams WHERE ClassId IN (SELECT k.Id FROM curriculum.AcademicClasses k JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM assessment.WrittenQuestions WHERE ChapterId IN (SELECT ch.Id FROM curriculum.Chapters ch JOIN curriculum.Subjects s ON ch.SubjectId = s.Id JOIN curriculum.AcademicClasses k ON s.ClassId = k.Id JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM assessment.Questions WHERE ChapterId IN (SELECT ch.Id FROM curriculum.Chapters ch JOIN curriculum.Subjects s ON ch.SubjectId = s.Id JOIN curriculum.AcademicClasses k ON s.ClassId = k.Id JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM content.Lessons WHERE ChapterId IN (SELECT ch.Id FROM curriculum.Chapters ch JOIN curriculum.Subjects s ON ch.SubjectId = s.Id JOIN curriculum.AcademicClasses k ON s.ClassId = k.Id JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM curriculum.Chapters WHERE SubjectId IN (SELECT s.Id FROM curriculum.Subjects s JOIN curriculum.AcademicClasses k ON s.ClassId = k.Id JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM curriculum.Subjects WHERE ClassId IN (SELECT k.Id FROM curriculum.AcademicClasses k JOIN curriculum.Curricula c ON k.CurriculumId = c.Id JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM curriculum.AcademicClasses WHERE CurriculumId IN (SELECT c.Id FROM curriculum.Curricula c JOIN curriculum.CurriculumBoards b ON c.BoardId = b.Id WHERE b.Code LIKE 'QA-E2E-%%')",
    "DELETE FROM curriculum.Curricula WHERE BoardId IN (SELECT Id FROM curriculum.CurriculumBoards WHERE Code LIKE 'QA-E2E-%%')",
    "DELETE FROM curriculum.CurriculumBoards WHERE Code LIKE 'QA-E2E-%%'",
    # Accounts last (roles, logins, tokens cascade)
    "DELETE FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test'",
]

def cleanup():
    with sql() as cn, cn.cursor() as cur:
        for stmt in CLEANUP:
            cur.execute(stmt, {"start": START}) if "%(start)s" in stmt else cur.execute(stmt.replace("%%", "%"))
        cn.commit()
        cur.execute("SELECT COUNT(*) AS n FROM [identity].Users WHERE Email LIKE 'qa-%@googols.test'")
        users = cur.fetchone()["n"]
        cur.execute("SELECT COUNT(*) AS n FROM curriculum.CurriculumBoards WHERE Code LIKE 'QA-E2E-%'")
        boards = cur.fetchone()["n"]
    print(f"CLEANUP: QA users left={users}, QA boards left={boards}")

PHOTO = None  # filled from tests/live/question.jpg

def main():
    global PHOTO
    with open(os.path.join(os.path.dirname(__file__), "question.jpg"), "rb") as f:
        PHOTO = "data:image/jpeg;base64," + base64.b64encode(f.read()).decode()

    admin, s1, s2, teacher, sadmin, parent = (Client(x) for x in ("admin", "stu1", "stu2", "teacher", "sadmin", "parent"))
    for c in (admin, s1, s2, teacher, sadmin, parent):
        check(f"register {c.label}", register(c) == 200)
    grant_role(admin.email, "Admin")
    check("admin login", login(admin) == 200)

    # ---- QA syllabus: board → year → class → subject → 2 chapters, lessons, questions, a written question
    code = f"QA-E2E-{RUN.upper()}"
    st, b = admin.call("POST", "/curriculum/boards", {"name": "QA board", "code": code, "countryCode": "IN", "isEntranceExam": False})
    check("create QA board", st == 201, st)
    st, cur = admin.call("POST", "/curriculum/admin/curricula", {"boardId": b["id"], "name": "QA syllabus", "academicYear": "2026-27"})
    st, cls = admin.call("POST", "/curriculum/admin/classes", {"curriculumId": cur["id"], "gradeNumber": 8, "gradeName": "Class 8"})
    st, sub = admin.call("POST", "/curriculum/admin/subjects", {"classId": cls["id"], "name": "Science", "code": "SCI-8", "mediumLanguage": "en", "sequence": 1})
    chapters = []
    for i, title in enumerate(("Photosynthesis", "Cells"), 1):
        st, ch = admin.call("POST", "/curriculum/chapters", {"subjectId": sub["id"], "title": title, "sequence": i})
        chapters.append(ch["id"])
    check("QA syllabus built", len(chapters) == 2 and st == 201)
    st, _ = admin.call("PUT", f"/curriculum/admin/curricula/{cur['id']}/status", {"status": "Published"})
    for ch in chapters:
        admin.call("PUT", f"/curriculum/admin/chapters/{ch}/status", {"status": "Published"})
    lessons = []
    bodies = ["Green plants make their own food by photosynthesis. Leaves contain chlorophyll, which absorbs sunlight. "
              "Plants take in carbon dioxide from the air through stomata and water from the soil. Using light energy they make glucose and give out oxygen.",
              "All living things are made of cells. A plant cell has a cell wall, a cell membrane, cytoplasm, a nucleus and chloroplasts. Animal cells have no cell wall."]
    for ch, body in zip(chapters, bodies):
        st, l = admin.call("POST", f"/curriculum/admin/chapters/{ch}/lessons", {"title": "Lesson 1", "body": body, "language": "en", "estimatedMinutes": 5, "kind": "ApprovedMaterial", "sequence": 1})
        admin.call("PUT", f"/curriculum/admin/lessons/{l['id']}/status", {"status": "Published"})
        lessons.append(l["id"])
    qn = 0
    for ch, n in ((chapters[0], 6), (chapters[1], 4)):
        for i in range(n):
            st, q = admin.call("POST", f"/curriculum/admin/chapters/{ch}/questions",
                               {"prompt": f"QA question {i + 1}?", "options": ["Right", "Wrong", "Also wrong"], "correctIndex": 0, "explanation": "QA", "difficulty": 1 + i % 3, "language": "en"})
            if st == 201 and admin.call("PUT", f"/curriculum/admin/questions/{q['id']}/status", {"status": "Published"})[0] == 204:
                qn += 1
    check("10 MCQs published", qn == 10, qn)
    st, wq = admin.call("POST", f"/curriculum/admin/chapters/{chapters[0]}/written-questions",
                        {"prompt": "Explain how plants make food.", "modelAnswer": "By photosynthesis: chlorophyll absorbs sunlight; CO2 and water make glucose and oxygen.",
                         "rubric": [{"criterion": "Names photosynthesis", "marks": 1}, {"criterion": "Mentions sunlight or chlorophyll", "marks": 1}, {"criterion": "Mentions glucose or oxygen", "marks": 1}], "language": "en"})
    check("written question created", st == 201, st)
    check("written question rejects empty rubric", admin.call("POST", f"/curriculum/admin/chapters/{chapters[0]}/written-questions", {"prompt": "x", "rubric": []})[0] == 400)
    admin.call("PUT", f"/curriculum/admin/written-questions/{wq['id']}/status", {"status": "Published"})

    # ---- Admin: golden-set eval + mock exam + schools
    st, ev = admin.call("POST", "/admin/ai/evals/cases", {"subjectId": sub["id"], "chapterId": chapters[0], "question": "QA-E2E: What is photosynthesis?",
                                                          "language": "en", "mode": "PersonalTeacher", "expectedKeywords": "chlorophyll\nglucose|sugar", "expectGrounded": True})
    check("eval case created", st == 201, st)
    st, run = admin.call("POST", "/admin/ai/evals/run")
    mine = [r for r in (run or {}).get("results", []) if r["caseId"] == ev["id"]] if st == 200 else []
    check("eval run scores the answer (grounded, checks listed)", st == 200 and mine and mine[0]["grounded"] and len(mine[0]["checks"]) >= 3, st)
    st, mock = admin.call("POST", "/curriculum/admin/exams", {"classId": cls["id"], "subjectId": None, "title": "QA mock", "chapterIds": chapters,
                                                              "mcqCount": 5, "writtenCount": 0, "durationMinutes": 10, "opensAtUtc": None, "closesAtUtc": None})
    check("mock exam created (draft)", st == 201, st)
    check("mock exam with bad timing rejected", admin.call("POST", "/curriculum/admin/exams", {"classId": cls["id"], "title": "x", "chapterIds": chapters, "mcqCount": 5,
        "writtenCount": 0, "durationMinutes": 1})[0] == 400)
    admin.call("PUT", f"/curriculum/admin/exams/{mock['id']}/status", {"status": "Published"})
    st, school = admin.call("POST", "/admin/schools", {"name": "QA School", "code": f"QAE2E{RUN.upper()}", "city": "Kochi"})
    st2, school_b = admin.call("POST", "/admin/schools", {"name": "QA Other School", "code": f"QAE2EB{RUN.upper()}", "city": None})
    check("schools created", st == 201 and st2 == 201)
    check("school admin must have an account", admin.call("POST", f"/admin/schools/{school['id']}/admins", {"email": f"nobody-{RUN}@googols.test"})[0] == 400)
    check("school admin assigned", admin.call("POST", f"/admin/schools/{school['id']}/admins", {"email": sadmin.email})[0] == 204)

    # ---- Students onboard (adults, so no parental consent step)
    for c, name in ((s1, "Anu QA"), (s2, "Ravi QA")):
        check(f"{c.label} login", login(c) == 200)
        st, _ = c.call("POST", "/students/me/onboarding", {"displayName": name, "dateOfBirth": "2000-01-01", "preferredLanguage": "en", "curriculumId": cur["id"], "classId": cls["id"]})
        check(f"{c.label} onboarded", st == 201, st)

    # ---- AI teacher: streaming, feedback, bookmark, photo, voice
    st, events = s1.sse("/ai/messages/stream", {"subjectId": sub["id"], "chapterId": chapters[0], "message": "How do plants make their food?", "mode": "PersonalTeacher", "language": "en"})
    kinds = [e for e, _ in events]
    done = next((d for e, d in events if e == "done"), None)
    check("AI answer streams (start, several deltas, done)", st == 200 and kinds[:1] == ["start"] and kinds.count("delta") >= 3 and done is not None, f"{st} {kinds[:5]}")
    streamed = "".join(d["text"] for e, d in events if e == "delta")
    check("streamed text matches the final reply", done and done["reply"] == streamed)
    check("answer grounded in the QA lesson", done and done["groundedInApprovedContent"])
    mid = done["messageId"] if done else None
    check("thumbs down with a comment saved", s1.call("POST", f"/ai/messages/{mid}/feedback", {"helpful": False, "comment": "QA comment"})[0] == 204)
    check("other students can't rate this answer", s2.call("POST", f"/ai/messages/{mid}/feedback", {"helpful": True})[0] == 404)
    check("answer bookmarked", s1.call("POST", "/learning/bookmarks", {"kind": "answer", "refId": mid})[0] == 201)
    check("lesson bookmarked", s1.call("POST", "/learning/bookmarks", {"kind": "lesson", "refId": lessons[0]})[0] == 201)
    st, bm = s1.call("GET", "/learning/bookmarks")
    check("bookmarks listed (2)", st == 200 and len(bm) == 2, bm)
    check("can't bookmark someone else's answer", s2.call("POST", "/learning/bookmarks", {"kind": "answer", "refId": mid})[0] == 404)

    st, photo = s1.call("POST", "/ai/messages", {"subjectId": sub["id"], "message": "", "mode": "DoubtSolver", "language": "en", "imageBase64": PHOTO})
    check("photo question answered (12 × 7 = 84)", st == 200 and "84" in photo["reply"], (st, str(photo)[:200]))
    conv = photo.get("conversationId") if st == 200 else None
    st, c_ = s1.call("GET", f"/ai/conversations/{conv}")
    user_msg = next((m for m in (c_ or {}).get("messages", []) if m["role"] == "user"), None)
    check("photo saved with the question", st == 200 and user_msg and user_msg["hasImage"])
    if user_msg:
        st, img, hdr = s1.call("GET", f"/ai/messages/{user_msg['id']}/image", raw=True)
        check("student can open their photo", st == 200 and hdr.get("Content-Type", "").startswith("image/jpeg"))
        check("other students can't open it", s2.call("GET", f"/ai/messages/{user_msg['id']}/image", raw=True)[0] == 404)
    check("non-image upload rejected", s1.call("POST", "/ai/messages", {"subjectId": sub["id"], "message": "hi", "mode": "DoubtSolver",
        "imageBase64": base64.b64encode(b"%PDF-1.4 not an image at all....").decode()})[0] == 400)
    check("empty question without photo rejected", s1.call("POST", "/ai/messages", {"subjectId": sub["id"], "message": " ", "mode": "DoubtSolver"})[0] == 400)
    st, sp = s1.call("GET", "/ai/speech/status")
    check("speech status reported", st == 200 and "serverVoice" in sp)
    st_sp = s1.call("POST", "/ai/speech", {"text": "Hello", "language": "ml"}, raw=True)[0]
    check("server voice answers 200 (set up) or 503 (not set up)", st_sp == (200 if sp.get("serverVoice") else 503), st_sp)

    # ---- Timed practice test with a written answer
    st, att = s1.call("POST", "/learning/practice-tests", {"subjectId": sub["id"], "chapterIds": [chapters[0]], "questionCount": 5, "minutes": 5, "includeWritten": True})
    check("practice test started (5 MCQ + 1 written, 5 min)", st == 200 and len(att["questions"]) == 5 and len(att["written"]) == 1, st)
    check("questions don't leak answers", st == 200 and all("correctIndex" not in q for q in att["questions"]))
    aid = att["attemptId"]
    check("bad practice test rejected", s1.call("POST", "/learning/practice-tests", {"subjectId": sub["id"], "chapterIds": [], "questionCount": 3, "minutes": 1, "includeWritten": False})[0] == 400)
    answers = [0, 0, 0, 0, 1]
    written = [{"questionId": att["written"][0]["id"], "text": "Plants make food by photosynthesis. Chlorophyll in leaves absorbs sunlight, and carbon dioxide and water make glucose and oxygen."}]
    check("answers autosaved", s1.call("PUT", f"/learning/exam-attempts/{aid}/answers", {"answers": answers, "written": written})[0] == 204)
    check("wrong answer count rejected", s1.call("PUT", f"/learning/exam-attempts/{aid}/answers", {"answers": [0]})[0] == 400)
    check("other students can't see the attempt", s2.call("GET", f"/learning/exam-attempts/{aid}")[0] == 404)
    st, res = s1.call("POST", f"/learning/exam-attempts/{aid}/submit", {"answers": answers, "written": written})
    w = res["written"][0] if st == 200 and res.get("written") else {}
    check("test graded: 4/5 MCQ", st == 200 and res["mcqScore"] == 4 and res["mcqTotal"] == 5, (st, str(res)[:200]))
    check("written answer AI-marked, provisional, with criteria", w.get("status") == "Provisional" and w.get("score") is not None and len(w.get("criteria", [])) == 3, w)
    check("test pays XP", st == 200 and res["xpAwarded"] > 0)
    check("resubmit refused", s1.call("POST", f"/learning/exam-attempts/{aid}/submit", {"answers": answers})[0] == 409)

    # ---- Lesson, quiz, mastery, plan, missions
    st, _ = s1.call("POST", f"/learning/lessons/{lessons[0]}/complete")
    st, quiz = s1.call("POST", f"/learning/chapters/{chapters[1]}/quiz")
    st, qres = s1.call("POST", f"/learning/quiz-attempts/{quiz['attemptId']}/submit", {"answers": [0] * len(quiz["questions"])})
    check("quiz on chapter 2 = 100%", st == 200 and qres["percent"] == 100, st)
    st, mastery = s1.call("GET", "/learning/mastery")
    chs = {c["chapterId"]: c for s in mastery["subjects"] for c in s["chapters"]} if st == 200 else {}
    check("mastery for both chapters", chs.get(chapters[0], {}).get("mastery") is not None and chs.get(chapters[1], {}).get("mastery") is not None, chs)
    st, plan = s1.call("GET", "/learning/study-plan")
    check("7-day study plan", st == 200 and len(plan) == 7)
    st, m = s1.call("GET", "/learning/missions")
    check("today's missions all done (lesson, practice, 80%+)", st == 200 and m["allDone"], m)
    st, claim = s1.call("POST", "/learning/missions/claim")
    check("mission bonus paid once", st == 200 and claim["xpAwarded"] > 0, st)
    st, claim2 = s1.call("POST", "/learning/missions/claim")
    check("second claim pays nothing", st == 200 and claim2["xpAwarded"] == 0)
    check("unfinished mission claim refused for another student", s2.call("POST", "/learning/missions/claim")[0] == 409)

    # ---- Mock exam: one attempt only
    st, exams = s1.call("GET", "/learning/exams")
    check("mock exam listed for the class", st == 200 and any(e["id"] == mock["id"] for e in exams))
    st, ma = s1.call("POST", f"/learning/exams/{mock['id']}/start")
    st2, mr = s1.call("POST", f"/learning/exam-attempts/{ma['attemptId']}/submit", {"answers": [0] * len(ma["questions"])})
    check("mock exam taken", st == 200 and st2 == 200)
    check("mock exam can't be retaken", s1.call("POST", f"/learning/exams/{mock['id']}/start")[0] == 409)

    # ---- School admin: division, staff, allocation, timetable, isolation
    check("sadmin login", login(sadmin) == 200)
    st, ov = sadmin.call("GET", "/school")
    check("school admin sees their school", st == 200 and ov["id"] == school["id"], st)
    check("…and not another school", sadmin.call("GET", f"/school?schoolId={school_b['id']}")[0] == 404)
    check("students can't open school admin", s1.call("GET", "/school")[0] == 403)
    st, div = sadmin.call("POST", "/school/divisions", {"classId": cls["id"], "name": "8A"})
    check("division created with a code", st == 201 and len(div["joinCode"]) == 6, st)
    check("duplicate division name refused", sadmin.call("POST", "/school/divisions", {"classId": cls["id"], "name": "8A"})[0] == 400)
    check("staff needs an existing account", sadmin.call("POST", "/school/staff", {"email": f"nobody-{RUN}@googols.test"})[0] == 400)
    check("teacher added to staff", sadmin.call("POST", "/school/staff", {"email": teacher.email})[0] == 204)
    st, staff = sadmin.call("GET", "/school/staff")
    tid = next(p["userId"] for p in staff if p["email"] == teacher.email)
    st, alloc = sadmin.call("POST", f"/school/divisions/{div['id']}/allocations", {"subjectId": sub["id"], "teacherUserId": tid})
    check("subject teacher allocated (classroom made)", st == 201, st)
    slots = [{"dayOfWeek": 1, "period": 1, "subjectId": sub["id"], "teacherUserId": None, "startTime": "09:00", "endTime": "09:45", "label": None},
             {"dayOfWeek": 1, "period": 2, "subjectId": None, "teacherUserId": None, "startTime": "09:45", "endTime": "10:00", "label": "Break"}]
    check("timetable saved", sadmin.call("PUT", f"/school/divisions/{div['id']}/timetable", {"slots": slots})[0] == 204)
    check("bad timetable day refused", sadmin.call("PUT", f"/school/divisions/{div['id']}/timetable", {"slots": [{**slots[0], "dayOfWeek": 7}]})[0] == 400)
    st, tt = sadmin.call("GET", f"/school/divisions/{div['id']}/timetable")
    check("teacher filled in from the allocation", st == 200 and tt[0]["teacherUserId"] == tid, tt)

    # ---- Student joins the division with its code
    st, j = s1.call("POST", "/students/me/classrooms/join", {"code": div["joinCode"]}); auth_pause()
    check("student joins division by code", st == 200, (st, j))
    st, my_tt = s1.call("GET", "/learning/timetable")
    check("student sees the timetable (2 periods)", st == 200 and len(my_tt) == 1 and len(my_tt[0]["slots"]) == 2, my_tt)
    st, rooms = s1.call("GET", "/students/me/classrooms")
    check("student is in the subject class", st == 200 and any(r["id"] == alloc["classroomId"] for r in rooms))

    # ---- Teacher: heat map, homework, class test, marking
    check("teacher login", login(teacher) == 200)
    st, trooms = teacher.call("GET", "/teacher/classrooms")
    room = next((r for r in (trooms or []) if r["id"] == alloc["classroomId"]), None)
    check("teacher sees the school class with 1 student", room and room["studentCount"] == 1, trooms)
    check("school class can't be opened for joining", teacher.call("PUT", f"/teacher/classrooms/{alloc['classroomId']}/joining", {"open": True})[0] == 404)
    st, hm = teacher.call("GET", f"/teacher/classrooms/{alloc['classroomId']}/heatmap")
    check("heat map: 1 student × 2 chapters, filled", st == 200 and len(hm["students"]) == 1 and len(hm["chapters"]) == 2 and all(v is not None for v in hm["students"][0]["cells"]), hm)
    st, tl = teacher.call("GET", f"/teacher/classrooms/{alloc['classroomId']}/lessons")
    st1, hw1 = teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/assignments", {"title": "Read lesson 1", "kind": "Lesson", "lessonId": tl[0]["id"]})
    st2, hw2 = teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/assignments", {"title": "Draw a leaf", "kind": "Task", "instructions": "Label it.",
                                                                                                  "dueAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() + 86400))})
    check("homework set (lesson + task)", st1 == 201 and st2 == 201, (st1, st2))
    check("homework with a past due date refused", teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/assignments",
        {"title": "x", "kind": "Task", "dueAtUtc": "2020-01-01T00:00:00Z"})[0] == 400)
    st, ct = teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/exams", {"classId": "00000000-0000-0000-0000-000000000000", "title": "QA class test",
        "chapterIds": chapters, "mcqCount": 5, "writtenCount": 0, "durationMinutes": 10})
    check("class test published", st == 201, st)
    st, hw = s1.call("GET", "/learning/homework")
    lesson_hw = next((h for h in hw if h["id"] == hw1["id"]), {})
    check("lesson homework already done (finished earlier)", st == 200 and lesson_hw.get("done"), hw)
    check("task homework marked done with a note", s1.call("POST", f"/learning/homework/{hw2['id']}/done", {"text": "QA note"})[0] == 204)
    check("lesson homework can't be ticked by hand", s1.call("POST", f"/learning/homework/{hw1['id']}/done", {})[0] == 409)
    st, tas = teacher.call("GET", f"/teacher/classrooms/{alloc['classroomId']}/assignments")
    check("teacher sees 2/2 done", st == 200 and all(a["done"] == 1 and a["total"] == 1 for a in tas), tas)
    st, exams = s1.call("GET", "/learning/exams")
    check("class test reaches the student", st == 200 and any(e["id"] == ct["id"] for e in exams))
    st, mark = teacher.call("GET", f"/teacher/classrooms/{alloc['classroomId']}/marking")
    item = next((x for x in (mark or []) if x["attemptId"] == aid), None)
    check("written answer waiting for the teacher", st == 200 and item and item["status"] == "Provisional", mark)
    check("mark out of range refused", teacher.call("PUT", f"/teacher/written-answers/{item['answerId']}", {"score": 9})[0] == 400)
    check("teacher confirms 2.5/3", teacher.call("PUT", f"/teacher/written-answers/{item['answerId']}", {"score": 2.5, "comment": "Good, add oxygen."})[0] == 204)
    st, view = s1.call("GET", f"/learning/exam-attempts/{aid}")
    check("student sees the confirmed mark", st == 200 and view["result"]["written"][0]["status"] == "Confirmed" and view["result"]["written"][0]["score"] == 2.5, view and view.get("result"))
    check("another teacher can't mark it", sadmin.call("PUT", f"/teacher/written-answers/{item['answerId']}", {"score": 0})[0] == 404)

    # ---- Parent weekly report
    st, pc = s1.call("POST", "/students/me/parent-code")
    check("parent login", login(parent) == 200)
    st, _ = parent.call("POST", "/parents/link", {"code": pc["code"], "relationship": "mother"}); auth_pause()
    check("parent linked", st == 200, st)
    st, rep = parent.call("GET", f"/parents/children/{s1_student_id(s1)}/weekly-report")
    check("weekly report: lessons, tests, homework", st == 200 and rep["lessonsCompleted"] >= 1 and rep["quizzes"] + rep["tests"] >= 3 and rep["homeworkSet"] >= 1, rep)
    check("report has no chat text (count only)", st == 200 and isinstance(rep["aiQuestions"], int) and "messages" not in rep)
    check("weekly email turned off", parent.call("PUT", f"/parents/children/{rep['studentId']}/report-email", {"enabled": False})[0] == 204)
    check("unlinked adult can't read the report", s2.call("GET", f"/parents/children/{rep['studentId']}/weekly-report")[0] == 404)

    # ---- Admin usage dashboard
    st, usage = admin.call("GET", "/admin/ai/usage?days=1")
    feats = {f["feature"] for f in usage["byFeature"]} if st == 200 else set()
    check("usage log has chat, photo, marking and eval calls with cost", {"teacher", "teacher-photo", "marking", "eval"} <= feats and usage["totals"]["costUsd"] > 0, feats)
    check("not-helpful answer listed with comment", st == 200 and any(f["comment"] == "QA comment" for f in usage["recentNotHelpful"]))
    check("students can't read usage", s1.call("GET", "/admin/ai/usage")[0] == 403)

    # ---- Removing a teacher closes their school class; leaving the division
    check("teacher removed from staff", sadmin.call("DELETE", f"/school/staff/{tid}")[0] == 204)
    st, trooms = teacher.call("GET", "/teacher/classrooms")
    check("their school class is gone", st == 200 and not any(r["id"] == alloc["classroomId"] for r in trooms))
    st, rooms = s1.call("GET", "/students/me/classrooms")
    check("student leaves the division", s1.call("DELETE", f"/students/me/classrooms/{alloc['classroomId']}")[0] == 204)
    st, my_tt = s1.call("GET", "/learning/timetable")
    check("timetable gone after leaving", st == 200 and my_tt == [])

def s1_student_id(c):
    return c.call("GET", "/students/me")[1]["id"]

if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # report and still clean up
        import traceback; traceback.print_exc()
        check("script finished without an exception", False, repr(ex))
    finally:
        try:
            cleanup()
        except Exception as ex:
            print("CLEANUP FAILED:", ex)
        passed = sum(1 for _, ok in results if ok)
        print(f"\nRESULT {passed}/{len(results)} passed")
        sys.exit(0 if passed == len(results) else 1)
