# Development Roadmap

Each phase ends with: build green, tests green, docs and `IMPLEMENTATION_STATUS.md` updated, and a walkthrough of the feature in the browser.

| Phase | Scope | Exit criteria |
|---|---|---|
| 1 Inspect | Reviewed existing `Student` repo (MVC 5/.NET Framework 4.8). Decision: new project on the blueprint stack, with React instead of Angular. Old app stays as reference and pilot. | Done |
| 2 Design | These 10 docs | Done |
| **3 Foundations** | Solution, layers, DbContext, Identity, adapters (OpenAI, Search, Blob, Redis, Hangfire, SignalR, OpenTelemetry), React shell | **Code written; first `dotnet build`, migration and test run pending on a machine with the .NET 10 SDK** |
| 4 Auth | Email confirmation + password reset emails (SMTP/ACS), parent account type, parent–child link with OTP + consent, refresh in SPA, role admin | Parent can consent; child unlocks AI |
| 5 Curriculum & content | Full admin catalog UI (create/edit/publish/archive/version), CSV/JSON import, content upload to Blob, review workflow, Worker job that chunks published content into AI Search | Kerala Class 8 Biology chapter published end-to-end |
| 6 Student portal | Lesson reader, progress, continue-learning, bookmarks, missions, language switch everywhere | Student completes a lesson; XP awarded |
| 7 AI Teacher complete | Saved conversations, streaming replies (SignalR), voice (Azure Speech ml-IN/en-IN), image doubts, Content Safety, usage/cost logs, feedback, golden-set evals | Malayalam photosynthesis journey passes evals |
| 8 Exams & intelligence | Question bank, AI drafting → review → publish, timed tests, auto scoring, rubric scoring (provisional), mastery per topic, weak-area detection, study plans | 10-question quiz with feedback and next step |
| 9 Gamification | Badges, levels UI, streaks, weekly challenges, class-scoped opt-in leaderboards | No duplicate awards under load test |
| 10 Portals | Parent dashboard, teacher classes/assignments/heatmap, school admin (years, divisions, allocation, timetable) | School data isolation tests pass |
| 11 Commerce & admin | Plans, entitlements, Razorpay via abstraction, invoices (GST), coupons, admin dashboards, audit log | Paid upgrade works in Razorpay test mode |
| 12 Hardening | Security review, load test, OpenTelemetry dashboards, IaC (Bicep), GitHub Actions deploy, backup/restore drill | Pilot go-live |

## MVP cut (pilot)
Phases 3–8 for **Kerala State, Classes 8–10, Malayalam + English, 2–3 verified subjects**. Gamification basics (XP, streak) are already in. Parent consent (Phase 4) is a launch blocker; school and commerce can follow.

## Reusing the existing Student app
The old MVC app has working logic worth porting rather than rewriting from scratch: adaptive session engine, SM-2 spaced retrieval, mastery ledger, misconception notebook, PDF textbook ingestion and OCR, Class 1 Malayalam play units, exam planner, parent weekly report. Port each into the matching phase as Application-layer policies with their existing unit tests.
