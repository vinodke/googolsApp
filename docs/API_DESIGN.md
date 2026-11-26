# API Design

REST, versioned under `/api/v1`, JSON, OpenAPI document at `/openapi/v1.json` (Development).

## Conventions
- **Errors:** RFC 7807 problem details. Validation errors return `400` with `errors: { field: [messages] }`. Conflicts `409`, consent/ownership `403`, AI limit `429`, AI unavailable `503`.
- **Ownership:** student routes use `/me`, never a student id. Parent and teacher routes that take an id check a verified link or classroom membership on the server.
- **Never trusted from the client:** user id, school id, marks, XP, payment status.
- **Rate limits:** `auth` 10/min per IP; `ai` 6/min per user plus 40/day per student.
- **Auth:** cookie (`?useCookies=true`) for the web app; bearer + refresh tokens for mobile.

## Implemented endpoints
| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | /api/v1/auth/register | public | Create account (Identity) |
| POST | /api/v1/auth/login?useCookies=true | public | Sign in (cookie) or bearer tokens without the flag |
| POST | /api/v1/auth/refresh | public | Refresh a bearer token |
| POST | /api/v1/auth/logout | signed in | Clear cookie |
| GET/POST | /api/v1/auth/manage/info, /forgotPassword, /resetPassword, /confirmEmail, /manage/2fa | Identity | Account management |
| GET | /api/v1/curriculum/boards | signed in | Boards |
| GET | /api/v1/curriculum/boards/{boardId}/curricula | signed in | Published curricula |
| GET | /api/v1/curriculum/curricula/{id}/classes | signed in | Classes |
| GET | /api/v1/curriculum/classes/{id}/subjects | signed in | Subjects |
| GET | /api/v1/curriculum/subjects/{id}/chapters | signed in | Published chapters |
| GET | /api/v1/curriculum/chapters/{id}/topics | signed in | Topics |
| POST | /api/v1/curriculum/boards | ContentManager | Create board (Draft workflow follows) |
| POST | /api/v1/curriculum/chapters | ContentManager | Create chapter (Draft) |
| GET | /api/v1/students/me | signed in | Profile, enrollment, level, consent flag |
| POST | /api/v1/students/me/onboarding | signed in | Create profile + enrollment, grants Student role |
| PUT | /api/v1/students/me/language | signed in | Switch en/ml |
| POST | /api/v1/ai/messages | Student | Ask the AI teacher |
| GET | /api/v1/gamification/xp | Student | XP total, level, recent awards |
| WS | /hubs/learning | signed in | Realtime events: `xpAwarded` |
| GET | /health | public | SQL health |

### Example: ask the AI teacher
```http
POST /api/v1/ai/messages
{ "subjectId": "…", "chapterId": "…", "topicId": null,
  "message": "എനിക്ക് Photosynthesis മനസ്സിലാകുന്നില്ല", "mode": "PersonalTeacher", "language": "ml",
  "history": [] }

200 { "reply": "…", "sources": [{ "contentId": "…", "title": "…", "kind": "ApprovedMaterial" }],
      "groundedInApprovedContent": true, "remainingToday": 39 }
```

## Planned route groups
| Group | Phase |
|---|---|
| /curriculum/* publish, archive, import; /content (upload, review, publish) | 5 |
| /learning/lessons, /learning/progress, /learning/study-plans, /bookmarks | 6 |
| /ai/conversations (list, delete), /ai/voice, /ai/feedback | 7 |
| /questions, /assessments, /exams, /exam-attempts, /results, /analytics/student | 8 |
| /gamification/badges, /challenges, /leaderboards | 9 |
| /parents (links, consent), /teachers, /classrooms, /assignments, /schools, /analytics/parent, /analytics/teacher | 10 |
| /subscriptions, /payments (+ webhook), /notifications, /admin/* | 11 |

The React client's types (`frontend/googols-web/src/core/api/types.ts`) are hand-written for now; generate them from the OpenAPI document once Phase 6 contracts settle.
