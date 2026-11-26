# Implementation Status

**Updated:** 26 Nov 2026, end of Phase 3 (foundations), first pass.

**Status words:** *Verified* = built and run here. *Written* = code complete, not yet compiled. *Planned* = phase noted.

## Build and test
| Part | Status | Evidence |
|---|---|---|
| React app (`frontend/googols-web`) | **Verified** | `npm run build` passes (TypeScript strict); `npm test` 3/3 pass; lint 0 errors, 3 warnings (fast-refresh hints) |
| .NET solution (`Googols.slnx`) | **Written** | The workspace that generated it had no .NET SDK and no NuGet access, so nothing has been compiled yet. **First step on your PC:** `dotnet build` then `dotnet test`. Package versions were pinned without checking nuget.org; let Visual Studio update them if restore complains. |
| EF Core migration | Planned | Run `dotnet ef migrations add InitialFoundation` (see README) |
| Unit tests (.NET) | Written | 21 cases: XP policy, levels, consent age, prompt escaping, PII screen, validators |
| Architecture tests | Written | Domain/Application dependency rule |

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
