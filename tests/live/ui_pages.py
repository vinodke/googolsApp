#!/usr/bin/env python3
"""Opens every page of the live app in a headless browser, as each kind of user, with real data.

Builds a small QA world through the API (QA syllabus, school, division, teacher, student with
lessons/tests/AI chat/homework, linked parent, admin), copies each account's sign-in cookie into
a browser context, visits every page and checks it rendered: no error alert, no console errors,
real content on screen. Screenshots go to ~/ui-shots. Everything QA is deleted at the end.
Run through tests/live/run_ui.sh (opens and closes the SQL firewall rule).
"""
import os, sys, time, json
sys.path.insert(0, os.path.dirname(__file__))
import e2e_stages as E
from playwright.sync_api import sync_playwright

SHOTS = os.path.expanduser("~/ui-shots")
os.makedirs(SHOTS, exist_ok=True)
results = E.results
check = E.check

def build_world():
    admin, s1, teacher, sadmin, parent = (E.Client(x) for x in ("admin", "stu1", "teacher", "sadmin", "parent"))
    for c in (admin, s1, teacher, sadmin, parent):
        check(f"register {c.label}", E.register(c) == 200)
    E.grant_role(admin.email, "Admin")
    check("admin login", E.login(admin) == 200)
    code = f"QA-E2E-{E.RUN.upper()}"
    _, b = admin.call("POST", "/curriculum/boards", {"name": "QA board", "code": code, "countryCode": "IN", "isEntranceExam": False})
    _, cur = admin.call("POST", "/curriculum/admin/curricula", {"boardId": b["id"], "name": "QA syllabus", "academicYear": "2026-27"})
    _, cls = admin.call("POST", "/curriculum/admin/classes", {"curriculumId": cur["id"], "gradeNumber": 8, "gradeName": "Class 8"})
    _, sub = admin.call("POST", "/curriculum/admin/subjects", {"classId": cls["id"], "name": "Science", "code": "SCI-8", "mediumLanguage": "en", "sequence": 1})
    chapters, lessons = [], []
    for i, (title, body) in enumerate((("Photosynthesis", "Green plants make food by photosynthesis. Chlorophyll in leaves absorbs sunlight; carbon dioxide and water make glucose and oxygen."),
                                       ("Cells", "All living things are made of cells. Plant cells have a cell wall, membrane, cytoplasm, nucleus and chloroplasts.")), 1):
        _, ch = admin.call("POST", "/curriculum/chapters", {"subjectId": sub["id"], "title": title, "sequence": i})
        chapters.append(ch["id"])
        _, l = admin.call("POST", f"/curriculum/admin/chapters/{ch['id']}/lessons", {"title": f"{title} basics", "body": body, "language": "en", "estimatedMinutes": 5, "kind": "ApprovedMaterial", "sequence": 1})
        admin.call("PUT", f"/curriculum/admin/lessons/{l['id']}/status", {"status": "Published"})
        lessons.append(l["id"])
        for k in range(5):
            _, q = admin.call("POST", f"/curriculum/admin/chapters/{ch['id']}/questions", {"prompt": f"{title} question {k + 1}?", "options": ["Right", "Wrong", "Also wrong"], "correctIndex": 0, "difficulty": 1 + k % 3, "language": "en"})
            admin.call("PUT", f"/curriculum/admin/questions/{q['id']}/status", {"status": "Published"})
    admin.call("PUT", f"/curriculum/admin/curricula/{cur['id']}/status", {"status": "Published"})
    for ch in chapters:
        admin.call("PUT", f"/curriculum/admin/chapters/{ch}/status", {"status": "Published"})
    _, wq = admin.call("POST", f"/curriculum/admin/chapters/{chapters[0]}/written-questions", {"prompt": "Explain how plants make food.", "modelAnswer": "Photosynthesis: sunlight, CO2 and water make glucose and oxygen.",
        "rubric": [{"criterion": "Names photosynthesis", "marks": 1}, {"criterion": "Mentions sunlight", "marks": 1}], "language": "en"})
    admin.call("PUT", f"/curriculum/admin/written-questions/{wq['id']}/status", {"status": "Published"})
    _, mock = admin.call("POST", "/curriculum/admin/exams", {"classId": cls["id"], "title": "QA mock exam", "chapterIds": chapters, "mcqCount": 5, "writtenCount": 0, "durationMinutes": 10})
    admin.call("PUT", f"/curriculum/admin/exams/{mock['id']}/status", {"status": "Published"})
    admin.call("POST", "/admin/ai/evals/cases", {"subjectId": sub["id"], "chapterId": chapters[0], "question": "QA-E2E: What is photosynthesis?", "language": "en", "expectedKeywords": "chlorophyll", "expectGrounded": True})
    _, school = admin.call("POST", "/admin/schools", {"name": "QA School", "code": f"QAE2E{E.RUN.upper()}", "city": "Kochi"})
    admin.call("POST", f"/admin/schools/{school['id']}/admins", {"email": sadmin.email})

    check("sadmin login", E.login(sadmin) == 200)
    _, div = sadmin.call("POST", "/school/divisions", {"classId": cls["id"], "name": "8A"})
    sadmin.call("POST", "/school/staff", {"email": teacher.email})
    _, staff = sadmin.call("GET", "/school/staff")
    tid = next(p["userId"] for p in staff if p["email"] == teacher.email)
    _, alloc = sadmin.call("POST", f"/school/divisions/{div['id']}/allocations", {"subjectId": sub["id"], "teacherUserId": tid})
    sadmin.call("PUT", f"/school/divisions/{div['id']}/timetable", {"slots": [
        {"dayOfWeek": d, "period": p, "subjectId": sub["id"] if p != 3 else None, "label": "Break" if p == 3 else None, "startTime": f"{8 + p}:00", "endTime": f"{8 + p}:45"}
        for d in range(1, 6) for p in range(1, 5)]})

    check("stu1 login", E.login(s1) == 200)
    s1.call("POST", "/students/me/onboarding", {"displayName": "Anu QA", "dateOfBirth": "2000-01-01", "preferredLanguage": "en", "curriculumId": cur["id"], "classId": cls["id"]})
    s1.call("POST", "/students/me/classrooms/join", {"code": div["joinCode"]}); E.auth_pause()
    s1.call("POST", f"/learning/lessons/{lessons[0]}/complete")
    _, quiz = s1.call("POST", f"/learning/chapters/{chapters[1]}/quiz")
    s1.call("POST", f"/learning/quiz-attempts/{quiz['attemptId']}/submit", {"answers": [0, 1, 0, 0, 0][:len(quiz["questions"])]})
    st, att = s1.call("POST", "/learning/practice-tests", {"subjectId": sub["id"], "chapterIds": [chapters[0]], "questionCount": 5, "minutes": 5, "includeWritten": True})
    check("setup: practice test started", st == 200, (st, str(att)[:300]))
    st, res = s1.call("POST", f"/learning/exam-attempts/{att['attemptId']}/submit", {"answers": [0, 0, 1, 0, 0][:len(att["questions"])],
        "written": [{"questionId": w["id"], "text": "Photosynthesis uses sunlight to make food."} for w in att["written"]]})
    check("setup: practice test submitted", st == 200, (st, str(res)[:300]))
    _, events = s1.sse("/ai/messages/stream", {"subjectId": sub["id"], "chapterId": chapters[0], "message": "What is chlorophyll?", "mode": "PersonalTeacher", "language": "en"})
    done = next((d for e, d in events if e == "done"), None)
    if done:
        s1.call("POST", "/learning/bookmarks", {"kind": "answer", "refId": done["messageId"]})
    s1.call("POST", "/learning/bookmarks", {"kind": "lesson", "refId": lessons[0]})

    check("teacher login", E.login(teacher) == 200)
    _, tl = teacher.call("GET", f"/teacher/classrooms/{alloc['classroomId']}/lessons")
    teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/assignments", {"title": "Read the cells lesson", "kind": "Lesson", "lessonId": tl[-1]["id"]})
    teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/assignments", {"title": "Draw a leaf", "kind": "Task", "instructions": "Label the parts.",
        "dueAtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() + 2 * 86400))})
    teacher.call("POST", f"/teacher/classrooms/{alloc['classroomId']}/exams", {"classId": cls["id"], "title": "QA class test", "chapterIds": chapters, "mcqCount": 5, "writtenCount": 1, "durationMinutes": 15})

    _, pc = s1.call("POST", "/students/me/parent-code")
    check("parent login", E.login(parent) == 200)
    parent.call("POST", "/parents/link", {"code": pc["code"], "relationship": "mother"}); E.auth_pause()
    student_id = s1.call("GET", "/students/me")[1]["id"]
    return dict(admin=admin, s1=s1, teacher=teacher, sadmin=sadmin, parent=parent, sub=sub["id"], chapters=chapters, lessons=lessons,
                attempt=att["attemptId"], classroom=alloc["classroomId"], division=div["id"], student=student_id)

def cookies(client):
    host = E.BASE.split("//")[1]
    return [{"name": c.name, "value": c.value, "domain": host, "path": c.path or "/", "httpOnly": True, "secure": True, "sameSite": "Lax"} for c in client.jar]

def visit(ctx, role, path, expect=None, clicks=()):
    page = ctx.new_page()
    errors = []
    # A 404 from /students/me is expected for accounts without a student profile (admin, teacher, parent).
    page.on("console", lambda m: errors.append(f"{m.text} @ {m.location.get('url', '')}") if m.type == "error" and "/students/me" not in m.location.get("url", "") else None)
    page.on("pageerror", lambda e: errors.append(str(e)))
    try:
        page.goto(E.BASE + path, wait_until="networkidle", timeout=45000)
        page.wait_for_timeout(800)
        for name in clicks:
            page.get_by_role("tab", name=name).click()
            page.wait_for_load_state("networkidle")
            page.wait_for_timeout(600)
            shot = f"{SHOTS}/{role}{path.replace('/', '_')}_{name.replace(' ', '-')}.png"
            page.screenshot(path=shot, full_page=True)
            alerts = [a for a in page.locator("[role=alert]").all_inner_texts() if a.strip()]
            check(f"{role} {path} › {name} tab", not alerts, alerts)
        page.screenshot(path=f"{SHOTS}/{role}{path.replace('/', '_') or '_root'}.png", full_page=True)
        text = page.inner_text("body")
        alerts = [a for a in page.locator("[role=alert]").all_inner_texts() if a.strip()]
        # Signed-out pages ask "who am I" and get 401 by design.
        real = [e for e in errors if "favicon" not in e and not (role == "public" and "auth/manage/info" in e)]
        ok = len(text.strip()) > 80 and not alerts and not real and (expect is None or expect in text)
        check(f"{role} {path}" + (f" shows '{expect}'" if expect else ""), ok, {"alerts": alerts, "console": real[:3], "url": page.url,
              "missing": None if expect is None or expect in text else expect, "text": None if ok else " ".join(text.split())[:300]})
    except Exception as ex:
        check(f"{role} {path}", False, repr(ex)[:200])
    finally:
        page.close()

def main():
    w = build_world()
    ch0, ch1 = w["chapters"]
    with sync_playwright() as p:
        browser = p.chromium.launch()
        def ctx_for(client=None):
            c = browser.new_context(viewport={"width": 1366, "height": 900})
            if client:
                c.add_cookies(cookies(client))
            return c

        anon = ctx_for()
        for path, expect in (("/", "AI teacher"), ("/home", "AI teacher"), ("/features", "Everything in Googols"), ("/how-it-works", "Learn in four steps"),
                             ("/about", "What we are"), ("/contact", "Send a message"), ("/privacy", "Privacy"), ("/login", "Sign in"), ("/register", "Create account")):
            visit(anon, "public", path, expect)
        anon.close()

        st = ctx_for(w["s1"])
        for path, expect in (("/", "Anu"), ("/subjects", "Science"), (f"/subjects/{w['sub']}", "Photosynthesis"), (f"/chapters/{ch0}", "Photosynthesis basics"),
                             (f"/lessons/{w['lessons'][0]}", "Chlorophyll"), (f"/chapters/{ch1}/quiz", "Start quiz"), ("/ai-teacher", "AI Teacher"),
                             ("/homework", "Draw a leaf"), ("/tests", "QA mock exam"), (f"/tests/attempts/{w['attempt']}", "Multiple choice"),
                             ("/progress", "Today's missions"), ("/saved", "Photosynthesis basics"), ("/achievements", "First step"), ("/family", "Classrooms"), ("/plans", "Premium")):
            visit(st, "student", path, expect)
        st.close()

        te = ctx_for(w["teacher"])
        visit(te, "teacher", "/teacher", "8A Science")
        visit(te, "teacher", f"/teacher/classrooms/{w['classroom']}", "Anu QA", clicks=("Heat map", "Homework", "Tests", "Marking"))
        te.close()

        sa = ctx_for(w["sadmin"])
        visit(sa, "school", "/school", "8A")
        visit(sa, "school", "/school/staff", w["teacher"].email)
        visit(sa, "school", f"/school/divisions/{w['division']}", "Anu QA", clicks=("Subject teachers", "Timetable", "Heat map"))
        sa.close()

        pa = ctx_for(w["parent"])
        visit(pa, "parent", "/parent", "Anu QA")
        visit(pa, "parent", f"/parent/children/{w['student']}/report", "Anu QA's week")
        visit(pa, "parent", "/parent/link", "Link your child")
        pa.close()

        ad = ctx_for(w["admin"])
        for path, expect, clicks in (("/admin", "QA board", ()), ("/admin/dashboard", "Students", ()), ("/admin/users", "qa-", ()), ("/admin/messages", "Inbox", ()),
                                     ("/admin/ai", "AI quality", ("Student ratings", "Test questions")), ("/admin/exams", "Mock exams", ()), ("/admin/schools", "QA School", ()),
                                     (f"/admin/chapters/{ch0}/lessons", "Photosynthesis basics", ()), (f"/admin/chapters/{ch0}/questions", "Explain how plants make food", ())):
            visit(ad, "admin", path, expect, clicks)
        ad.close()
        browser.close()

if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        import traceback; traceback.print_exc()
        check("ui script finished", False, repr(ex))
    finally:
        try:
            E.cleanup()
        except Exception as ex:
            print("CLEANUP FAILED:", ex)
        passed = sum(1 for _, ok in results if ok)
        print(f"\nRESULT {passed}/{len(results)} passed")
