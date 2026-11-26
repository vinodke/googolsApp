# Implementation Status

**Updated:** 26 Nov 2026 — Phase 3 foundations built, tested and **deployed to Azure**.

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
