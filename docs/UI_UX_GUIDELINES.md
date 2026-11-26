# UI/UX Guidelines

Tokens live in `frontend/googols-web/src/styles/tokens.css`; components use variables only, never raw colours.

## Direction
Modern, friendly, a little magical, gamified for students, calm for parents, efficient for teachers and admins. Mobile-first: most Kerala students will use a phone. Gradients and glow are used sparingly: the brand gradient appears on the hero card and logo only.

## Tokens
| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-primary` | Deep indigo #4338ca | #7c83ff | Primary actions, active nav |
| `--color-secondary` | Electric purple #8b5cf6 | — | Gradient partner, highlights |
| `--color-accent` | Cyan #06b6d4 | — | Progress, data accents |
| `--color-highlight` | Soft violet #ede9fe | #231d52 | Chips, badges, selected rows |
| `--color-bg` | #f8f9fc | Deep navy #0b1026 | Page |
| Spacing | 4 px scale: `--space-1`…`--space-8` | | |
| Radius | 8 / 12 / 20 / pill | | |
| Type | Inter + **Noto Sans Malayalam**, line height 1.6 (Malayalam needs room) | | |
| Motion | 120/200 ms, disabled under `prefers-reduced-motion` | | |

## Components (in `global.css`)
Buttons (`btn-primary`, `btn-ghost`, `btn-chip`), cards (`card`, `card-hero`), form fields with inline errors, badges, alert, empty state, skeleton loader, responsive grid. Planned: modal, table, tabs, toast, progress ring, chart wrappers.

## Rules
- Every screen has **loading, empty, error and success** states. No fake numbers.
- Touch targets ≥ 44 px; focus ring visible on every interactive element.
- Language toggle (മലയാളം / English) is always one tap away on learning screens.
- AI answers show where they came from: 📖 *From your lessons* vs *General explanation: not from your textbook*.
- Buttons that can't act yet are disabled with a reason, or not shown at all.
- Child-facing copy: short sentences, encouraging, never shaming.

## Portal layouts
| Portal | Layout | Built |
|---|---|---|
| Student | Left nav (desktop) / bottom tabs (≤760 px), top bar with level, XP, streak | Home, Subjects, Chapters, AI Teacher, Onboarding, Sign in/up |
| Parent | Child switcher, three-question summary: *how much, how well, what next* | Phase 10 |
| Teacher | Class list → heatmap → assign; dense tables | Phase 10 |
| Admin | Data tables with filters, audit trail | Phase 11 |

## Student navigation (target)
Home · My Learning · AI Teacher · Subjects · My Exams · Challenges · Leaderboard · Achievements · My Progress · Profile. Only routes with working pages appear in the nav.
