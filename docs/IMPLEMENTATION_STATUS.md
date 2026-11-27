# Implementation Status

**Updated:** 27 Nov 2026 — AI teacher upgrades, tests & progress, and schools/homework/parent reports built, deployed and live-tested.

**Status words:** *Verified* = built and run here. *Written* = code complete, not yet compiled. *Planned* = phase noted.

## Build and test
| Part | Status | Evidence |
|---|---|---|
| React app (`frontend/googols-web`) | **Verified** | `npm run build` passes (TypeScript strict); `npm test` 5/5 pass (API client + admin catalog screen); lint 0 errors |
| .NET solution (`Googols.slnx`) | **Verified** | Built with .NET SDK 10.0.404 in Azure Cloud Shell: 0 errors, 0 package-vulnerability warnings (packages updated to 10.0.15 / latest stable, transitive pins for Pkcs, Newtonsoft.Json, Microsoft.OpenApi) |
| EF Core migration | **Verified** | `InitialFoundation` generated and applied to Azure SQL. The migration files were generated in Cloud Shell and are in `googols-source-20261126.tgz` in the storage container (see Deployment) — copy `src/Googols.Infrastructure/Persistence/Migrations/` from it into your repo; don't regenerate, or EF will try to create the tables twice |
| Unit tests (.NET) | **Verified** | 32/32 pass: XP policy, levels, consent age, prompt escaping, PII screen, validators, admin catalog rules |
| Architecture tests | **Verified** | 2/2 pass: Domain/Application dependency rule |

## Deployment (Azure, verified 26 Nov 2026)
Live: **https://googols-app.azurewebsites.net** — `/health` returns Healthy (SQL reachable).

| Resource (all in `googols-rg`, Central India) | Details |
|---|---|
| App Service plan `googols-plan` | Linux, B1, always-on, HTTPS-only, TLS 1.2+, FTPS off |
| Web app `googols-app` | .NET 10, runs `Googols.WebApi.dll`; React build served from `wwwroot`; deployed via `WEBSITE_RUN_FROM_PACKAGE` (blob + read SAS, 1-year expiry) |
| Azure SQL `googols-sql-10298` / db `googols` | Basic 5 DTU, local backup redundancy; firewall allows Azure services only |
| Storage `googolsst10298` | Container `deploy` (app packages + source archive); app's Blob storage |
| AI | Existing `icoon-ai` Foundry (East US 2), deployment `gpt-4.1-mini`, key in app settings |

App settings hold secrets (SQL connection string, Blob connection string, AI key); nothing secret is in the repo. The SQL admin password was generated in Cloud Shell and only exists inside the connection-string app setting — reset it in the portal (SQL server → Reset password) if you need to log in with SSMS.

Live checks passed: anonymous calls to every protected API return 401; bad sign-in returns 401 problem details; SPA deep links return 200; HTTP→HTTPS 301; HSTS, CSP, nosniff, frame-deny headers present.

**Admin access:** app setting `Admin__BootstrapEmail=admin@gmail.com` makes that account SuperAdmin on every app start (it was also granted directly in the database on 26 Nov). Admins without a student profile land on `/admin`; admins with one see an Admin link in the student menu.

Redeploy: `bash infrastructure/azure/deploy.sh` from Cloud Shell (README). The Worker is not deployed yet (no background jobs exist until Phase 5).

## Live end-to-end test (26 Nov 2026, against googols-app)
Sample syllabus published: **KL-SCERT → Kerala State Syllabus 2026-27 → Class 8 → Biology (Malayalam medium) → Photosynthesis**.

| # | Step | Result |
|---|---|---|
| 1–2 | Register + sign in (cookie) | 200 / 200 |
| 3 | `/students/me` before onboarding | 404 (correct) |
| 4 | Student calling admin catalog API | 403 (correct) |
| 5–6 | Pick board → year → Class 8, onboard in Malayalam | 201 |
| 8 | Profile | KL-SCERT · Class 8 · 2026-27, level 1, consent not required (adult test account) |
| 9 | Chapters | ["Photosynthesis"] |
| 10 | AI Teacher, Malayalam question about photosynthesis | **200**, 1,208-char answer, 915 Malayalam letters, everyday examples; `grounded=false` (no textbook content indexed yet); 39 questions left today |
| 11 | Question containing a phone number | 422 blocked (correct) |
| 12 | XP | 0 (no lesson/quiz events exist yet) |

**Bug found and fixed by this test:** the API rejected `"mode":"PersonalTeacher"` with 400 because .NET reads enums as numbers by default; the React AI Teacher screen would have failed for every student. Fixed with `JsonStringEnumConverter` and redeployed. Test accounts were deleted afterwards; only `admin@gmail.com` remains.

## Admin screen + UI tests (26 Nov 2026)
Admin curriculum screen tested in the browser on the live site as `admin@gmail.com`, using a throwaway "QA-TEST" board (deleted from the database afterwards):

| Check | Result |
|---|---|
| Add board with lowercase code | Saved, code uppercased |
| Academic year "2026" | Rejected: "Use the form 2026-27." |
| Publish a year with no classes | Button disabled, "Add a class first" |
| Add Class 8, subject code "QA 8" | Rejected (spaces not allowed) |
| Subject code "qa-8" | Saved as "QA-8 · English medium" |
| Add chapter; publish / unpublish year and chapter | Works, status badge updates |

Automated React UI tests (vitest, mocked API): **10/10 pass** — admin screen, onboarding (correct request body, server validation error shown), AI Teacher (mode sent as text, Malayalam reply, "questions left today", 422 blocked message, locked when parental consent is missing).

**Bugs found and fixed (deployed as `app-202611261551.zip`):**
- AI Teacher picked English before the student profile had loaded, so Malayalam-medium students got English answers unless they changed it by hand. The language now follows the profile until the student chooses one.
- Published/Draft/Archived badges on the admin screen all showed the same colour (overridden by the global badge style). Fixed; verified green "Published" on the live site.

## Phases 4–12 (27 Nov 2026) — built, deployed, live-tested
Compiled in Azure Cloud Shell (.NET SDK 10.0.404): **0 errors, 0 warnings**. Tests: **73/73 unit, 2/2 architecture, 17/17 React UI**.
Migration `Phases4To12` (13 new tables + `StudentProfiles.ShowOnLeaderboard`, additive only) applied to Azure SQL. Deployed as `app-202611270158.zip`; `/health` 200.

Live end-to-end test against googols-app with throwaway accounts (all deleted afterwards, 0 QA users left): **56 checks passed**, including —
parent code (single-use, wrong code rejected) → consent (old notice version rejected) → AI unlocked; masked parent email for the student;
lesson publish + PDF attachment (fake PDF rejected); lesson complete = 130 XP (20 + chapter 100 + streak 10), repeat = 0;
AI answer **grounded in the published lesson** via SQL BM25, saved chat continued, self-harm → Tele-MANAS 14416 reply, phone number blocked;
quiz served without answers, graded on server (100% → +45 XP, 2 badges), resubmit rejected; leaderboard first name + initial;
teacher classroom join + progress, other users blocked; plans listed, checkout 503 until Razorpay keys are set, unsigned webhook 401;
admin metrics/users, SuperAdmin grant refused for Admin, user lock, chapter with lessons can't be deleted; sign-in rate limit (10/min/IP) confirmed.

| Phase | What's live |
|---|---|
| 4 Parents & consent | Student one-time code → parent links → consent notice v2026-11 (give/withdraw) → parent dashboard; SMTP sender for consent receipts and Identity emails |
| 5 Content | Admin lesson editor (text/markdown, .txt load, preview, PDF/PNG/JPEG attachment ≤20 MB checked by bytes); published text chunked for the AI teacher (SQL BM25; Azure AI Search optional) |
| 6 Lessons & progress | Chapter/lesson pages, mark complete, chapter bonus, daily streak, progress bars |
| 7 AI history & safety | Saved conversations; layered safety (contact info, self-harm care reply, Azure AI Content Safety when configured, provider filter) |
| 8 Quizzes | Question bank per chapter, AI-drafted questions for review, 10-question chapter quizzes graded server-side, XP once per chapter |
| 9 Rewards | 11 badges, class leaderboard (week/all, opt-out) |
| 10 Teachers | Classrooms with join codes, per-student progress, weakest chapters |
| 11 Plans | Free 15 AI/day; Premium ₹199/month or ₹1,499/year with 60 AI/day (**placeholder prices**, app settings `Billing__*`); Razorpay order + signature verify + webhook |
| 12 Admin | Metrics dashboard (14-day charts), users (roles, lock), curriculum rename/delete, CSV chapter import |

**To switch on:** `Speech__Region` + `Speech__ApiKey` (Malayalam read-aloud), `Razorpay__KeyId/KeySecret/WebhookSecret` (webhook URL `/api/v1/billing/webhooks/razorpay`), `Email__Smtp__Host/Port/User/Password` + `Email__From`, `ContentSafety__Endpoint/ApiKey`. Roles granted by an admin apply after the user signs in again.

## AI teacher, tests & progress, schools (27 Nov 2026) — built, deployed, live-tested
Three migrations, all additive: `AiTeacherUpgrades` (3 tables, 4 columns), `LearningAndExams` (6 tables), `SchoolsHomeworkReports` (9 tables, 4 columns).
Tests: **83/83 unit, 2/2 architecture, 28/28 React UI**. Live end-to-end script `tests/live/run.sh`: **105/105 checks passed** on the live site (throwaway QA accounts and a separate QA syllabus; afterwards 0 QA users and 0 QA boards left, firewall rule removed).

| Area | What's live |
|---|---|
| Streaming answers | `POST /ai/messages/stream` (server-sent events: start → delta… → done). The final reply is screened again and replaces the streamed text. |
| Photo doubts | JPEG/PNG up to 4 MB (shrunk in the browser to 1600 px), checked by file signature, sent to gpt-4.1-mini vision, stored privately in Blob `ai-photos`; only the student can open it. |
| Voice | Speak a question (browser speech recognition, ml-IN / en-IN). Read aloud: device voice, or Azure AI Speech (`ml-IN-SobhanaNeural`) when `Speech__Region` + `Speech__ApiKey` are set; until then Malayalam read-aloud needs a device Malayalam voice. |
| Ratings, usage, evals | Thumbs up/down (+ comment) per answer; `ai.UsageLogs` row per AI call with cost from `AiPricing__*` (default gpt-4.1-mini list prices, ₹85/$); Admin → AI quality: usage & cost by feature/day, "not helpful" answers, golden-set test questions with a Run button (language, key points, banned phrases, grounding). |
| Timed tests | Practice tests (student builds: chapters, 5–50 questions, 5–180 min, optional 2 written), mock exams (Admin → Mock exams, one attempt, open/close times), class tests (teacher). Server deadline + 60 s grace, autosave, auto-submit, late submissions use the last autosave. XP once per test / once a day for practice. |
| Written answers | Rubric (marking points, 1–10 marks each). AI marks against it (provisional, clamped, half marks); teacher confirms or changes in Classroom → Marking; student sees "provisional" / "confirmed". |
| Mastery & plan | Chapter mastery = moving average of quiz/test/written results (60% newest) blended with lessons done; weak areas (< 60%); 7-day plan (weak first, then unfinished, untested, stale). Page: My progress. |
| Missions & challenges | 3 daily missions (lesson, quiz/test, 80%+) → +20 XP once a day; 2 weekly challenges rotating from 4 → +50 XP each. All verified on the server. |
| Bookmarks | Save lessons and AI answers (Saved page). |
| Parent weekly report | Parent area → Weekly report (any week): lessons, scores, days active, XP, AI question count (no chat text), weak chapters, homework. Monday 7 am email of last week (once per parent/child/week, opt-out) when SMTP is configured. |
| Homework | Teacher sets lesson / quiz / timed test / other task with a due date; lesson, quiz and test homework ticks itself off; tasks are marked done by the student with a note. |
| Heat map | Teacher classroom and school division: students × chapters coloured by mastery, with class averages. |
| Schools | Admin → Schools (create, assign school admins). School admin (/school): staff (existing accounts), divisions with join codes, subject teachers (each becomes a linked classroom with the division's students), timetable editor (Mon–Sat, up to 10 periods). Students join with the division code and see the timetable; teachers see theirs. All /school routes are scoped to the admin's own school. |

## Google sign-in (27 Nov 2026)
"Continue with Google" on sign-in and sign-up. Google Cloud project **googols**, OAuth client "Googols web" (Web application), redirect URI `https://googols-app.azurewebsites.net/signin-google`, consent screen **In production** (scopes: openid, email, profile; no Google review needed). Keys live only in app settings `Authentication__Google__ClientId` / `__ClientSecret`.
A Google account links to an existing Googols account only when Google reports the email as verified; otherwise a new account is created. Locked accounts can't sign in through Google. Public privacy page at `/privacy` (plain-language draft; have it reviewed). When googols.in goes live, add `https://googols.in/signin-google` to the OAuth client's redirect URIs.
Also: role changes and locks now reach existing sessions within 1 minute (security-stamp check).

## By module
| Module | Status | Built | Next |
|---|---|---|---|
| Identity | Deployed | Register, login (cookie/bearer), refresh, logout, manage endpoints, lockout, 7 roles seeded, policies | Parent accounts, email sender, consent OTP (Phase 4) |
| Curriculum | **Deployed** | Board → Academic year (version, status) → Class → Subject → Chapter; student reads filtered to Published. **Admin screen at `/admin`**: add board, academic year, class, subject (medium), chapter; publish/unpublish years and chapters (a year can't be published without a class) | Edit/rename, topics, CSV import, content upload (Phase 5) |
| Student portal | Written (API) / Verified (UI build) | Onboarding (board → year → class → language), dashboard with real data and empty states, subjects, chapters, language switch | Lessons, progress (Phase 6) |
| AI Teacher | Written (API) / Verified (UI build) | Full orchestration pipeline, Azure OpenAI + AI Search adapters, 5 modes, ml/en, sources + grounded label, daily cap, consent gate, PII screen | Saved chats, streaming, voice, images, Content Safety (Phase 7) |
| Gamification | Written | XP ledger with idempotency, configurable rules seeded from blueprint, levels, SignalR `xpAwarded` push, XP summary API + UI | Badges, streak updates, leaderboards (Phase 9) |
| Background jobs | Written | Hangfire on SQL (`jobs` schema), Worker host, `IBackgroundJobs` | Content indexing job (Phase 5) |
| Storage / cache / realtime / telemetry | Written | Blob adapter, Redis/in-memory cache, SignalR hub with per-student groups, OpenTelemetry → Azure Monitor or OTLP | Dashboards (Phase 12) |
| Exams, parent, teacher, school, subscriptions, admin | Planned | — | Phases 8–11 |

## Known limitations
- `Auth:RequireConfirmedEmail` is `false` because no email sender exists yet; switch it on in app settings in Phase 4.
- No content is seeded yet: onboarding shows "No syllabus has been published yet" until an admin adds and publishes a board, academic year and class at `/admin`.
- The admin screen can add and publish but not rename or delete yet.
- AI Teacher answers are not grounded in textbook content until content is uploaded and indexed into Azure AI Search (Phase 5); until then every answer is labelled "General explanation".
- XP is awarded by `XpLedgerService`, but no learning flow calls it yet; lesson completion arrives in Phase 6.
- Streak days are stored but not yet updated.
- `BasicContentSafety` only screens contact details and length. Azure AI Content Safety is required before launch.
- AI conversation history is held in the browser for the session; server-side persistence comes in Phase 7.

## Decisions log
- **New project, not a migration of `Student`** (user decision, 26 Nov). The old app remains the pilot and a source of proven logic to port.
- **React instead of Angular** for the frontend (user decision); every other component follows blueprint §8.1.
- Modules as feature folders across layers instead of separate `Modules/*` projects (see SYSTEM_ARCHITECTURE).
- Question bank joined to assessments many-to-many (see DATABASE_DESIGN).
