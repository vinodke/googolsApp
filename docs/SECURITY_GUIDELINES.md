# Security, Privacy and Child Safety

Googols serves children, so these are product requirements rather than later hardening.

## Implemented in the foundation
| Control | Where |
|---|---|
| Identity with lockout (5 tries → 15 min), unique email, 8+ char passwords, email confirmation required in Production | `Program.cs` |
| HttpOnly cookie for the SPA; bearer/refresh for mobile; bearer on `/hubs` only via query string | `Program.cs` |
| Role policies: student, parent, teacher, content-manager, admin; 7 roles seeded | `Program.cs`, `Roles` |
| Ownership by construction: `/students/me`, AI subject must belong to the student's class | `StudentEndpoints`, `AiTeacherService` |
| Parental consent gate for under-18s (no DOB counts as under 18) before AI use | `StudentProfile.RequiresParentalConsent` |
| Server-only XP with idempotent ledger | `XpLedgerService`, unique index |
| Rate limits (auth, AI) + AI daily cap | `Program.cs`, `AiTeacherService` |
| Validation on every write (FluentValidation) | `Application/Curriculum/CurriculumDtos.cs` |
| Problem-details errors without stack traces | `GlobalExceptionHandler` |
| Security headers: nosniff, frame DENY, referrer policy, permissions policy, CSP on API | `UseSecurityHeaders` |
| HSTS + HTTPS redirect outside Development | `Program.cs` |
| No secrets in source: user-secrets locally, Key Vault in Azure; `.gitignore` covers secret files | README, `.gitignore` |
| Uploaded files get server-generated blob names; containers private | `BlobFileStorage` |
| Prompt-injection escaping and PII screen on AI input/output | `TeacherPromptBuilder`, `BasicContentSafety` |
| Architecture test keeps vendors out of domain logic | `Googols.ArchitectureTests` |

## Required before a public launch
1. **Legal review** under India's Digital Personal Data Protection Act 2023 and its Rules (verifiable parental consent for under-18s, no tracking or targeted ads to children, purpose limitation, erasure). Add GDPR/COPPA checks before international markets.
2. Parent verification flow: OTP to the parent's phone or email, consent text versioned (`ConsentVersion`), consent withdrawal.
3. Data export and erasure endpoints for a child's data.
4. Azure AI Content Safety on AI and uploads; malware scan on uploads (Defender for Storage); file type and size allow-list.
5. Audit log table for privileged actions (role changes, publishing, data export, grade overrides).
6. Production CSP for the SPA host, plus `SameSite=Strict` review on the auth cookie.
7. Payment provider webhooks verified by signature; never store card data.
8. Distributed rate limiting (Redis) once there's more than one API instance.
9. Penetration test, plus an authorization test suite: one test per endpoint proving another user's id returns 403/404.
10. Incident response and harmful-content escalation runbook.

## Rules for contributors
- Never trust ids, marks, XP, school or payment status from the client.
- Never log message text, passwords or tokens. Log ids and counts.
- Never put keys in React code or `appsettings.json`.
- Every new endpoint needs an authorization policy and an ownership check.
