# Implementation Status

**Updated:** 26 Nov 2026 — Phase 3 foundations built, tested and **deployed to Azure**.

**Status words:** *Verified* = built and run here. *Written* = code complete, not yet compiled. *Planned* = phase noted.

## Build and test
| Part | Status | Evidence |
|---|---|---|
| React app (`frontend/googols-web`) | **Verified** | `npm run build` passes (TypeScript strict); `npm test` 3/3 pass; lint 0 errors, 3 warnings (fast-refresh hints) |
| .NET solution (`Googols.slnx`) | **Verified** | Built with .NET SDK 10.0.404 in Azure Cloud Shell: 0 errors, 0 package-vulnerability warnings (packages updated to 10.0.15 / latest stable, transitive pins for Pkcs, Newtonsoft.Json, Microsoft.OpenApi) |
| EF Core migration | **Verified** | `InitialFoundation` generated and applied to Azure SQL. The migration files were generated in Cloud Shell and are in `googols-source-20261126.tgz` in the storage container (see Deployment) — copy `src/Googols.Infrastructure/Persistence/Migrations/` from it into your repo; don't regenerate, or EF will try to create the tables twice |
| Unit tests (.NET) | **Verified** | 21/21 pass: XP policy, levels, consent age, prompt escaping, PII screen, validators |
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

Redeploy: `bash infrastructure/azure/deploy.sh` from Cloud Shell (README). The Worker is not deployed yet (no background jobs exist until Phase 5).

## By module
| Module | Status | Built | Next |
|---|---|---|---|
| Identity | Written | Register, login (cookie/bearer), refresh, logout, manage endpoints, lockout, 7 roles seeded, policies | Parent accounts, email sender, consent OTP (Phase 4) |
| Curriculum | Written | Board → Curriculum(year, version, status) → Class → Subject → Chapter → Topic → Objective; read APIs filtered to Published; create board/chapter for content managers | Edit/publish/archive/import + admin UI (Phase 5) |
| Student portal | Written (API) / Verified (UI build) | Onboarding (board → year → class → language), dashboard with real data and empty states, subjects, chapters, language switch | Lessons, progress (Phase 6) |
| AI Teacher | Written (API) / Verified (UI build) | Full orchestration pipeline, Azure OpenAI + AI Search adapters, 5 modes, ml/en, sources + grounded label, daily cap, consent gate, PII screen | Saved chats, streaming, voice, images, Content Safety (Phase 7) |
| Gamification | Written | XP ledger with idempotency, configurable rules seeded from blueprint, levels, SignalR `xpAwarded` push, XP summary API + UI | Badges, streak updates, leaderboards (Phase 9) |
| Background jobs | Written | Hangfire on SQL (`jobs` schema), Worker host, `IBackgroundJobs` | Content indexing job (Phase 5) |
| Storage / cache / realtime / telemetry | Written | Blob adapter, Redis/in-memory cache, SignalR hub with per-student groups, OpenTelemetry → Azure Monitor or OTLP | Dashboards (Phase 12) |
| Exams, parent, teacher, school, subscriptions, admin | Planned | — | Phases 8–11 |

## Known limitations
- `Auth:RequireConfirmedEmail` is `false` because no email sender exists yet; switch it on in app settings in Phase 4.
- No content is seeded: onboarding shows "No syllabus has been published yet" until a content manager creates and publishes a board, curriculum and class. Phase 5 adds the admin UI and import; until then use the POST endpoints plus SQL to set `Status = 3` (Published).
- Curriculum write APIs exist only for boards and chapters.
- XP is awarded by `XpLedgerService`, but no learning flow calls it yet; lesson completion arrives in Phase 6.
- Streak days are stored but not yet updated.
- `BasicContentSafety` only screens contact details and length. Azure AI Content Safety is required before launch.
- AI conversation history is held in the browser for the session; server-side persistence comes in Phase 7.

## Decisions log
- **New project, not a migration of `Student`** (user decision, 26 Nov). The old app remains the pilot and a source of proven logic to port.
- **React instead of Angular** for the frontend (user decision); every other component follows blueprint §8.1.
- Modules as feature folders across layers instead of separate `Modules/*` projects (see SYSTEM_ARCHITECTURE).
- Question bank joined to assessments many-to-many (see DATABASE_DESIGN).
