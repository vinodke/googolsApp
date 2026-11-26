# Googols — Product Blueprint

## Vision
A personal AI-powered school for every student from Class 1 to Class 12 and entrance aspirants. **Every student should feel they have their own teacher who knows what they've learned, what they haven't understood, and exactly what to learn next.**

Start in Kerala (Kerala State syllabus, Malayalam and English), then expand to CBSE, ICSE/ISC, international curricula and NEET/JEE/KEAM/CUET through the same configurable curriculum engine.

## Users
| User | Main purpose | Portal tone |
|---|---|---|
| Student | Learn, practise, ask doubts, prepare for exams | Engaging, gamified |
| Parent | See progress, give consent, manage subscription | Simple, informative |
| Teacher | Classes, assignments, assessments, review AI content | Productive |
| School admin | Academic years, classes, teacher allocation, reports | Operational |
| Content manager | Curriculum catalog, content upload and approval | Data management |
| Platform admin | Users, AI configuration, subscriptions, audit | Operational visibility |

## Modules
| # | Module | Pillar | First shipped in |
|---|---|---|---|
| 1 | Identity & authentication | Foundation | Phase 3–4 |
| 2 | Curriculum management | Learn | Phase 5 |
| 3 | Student portal | Learn | Phase 6 |
| 4 | **AI Personal Teacher** (signature) | Learn | Phase 7 |
| 5 | Learning content management | Learn | Phase 5 |
| 6 | Examination centre | Practice | Phase 8 |
| 7 | Learning intelligence | Grow | Phase 8 |
| 8 | Gamification | Grow | Phase 9 |
| 9 | Parent portal | Grow | Phase 10 |
| 10 | Teacher portal | Practice | Phase 10 |
| 11 | School management | Foundation | Phase 10 |
| 12 | Administration | Foundation | Phase 11 |
| 13 | Subscriptions & payments | Foundation | Phase 11 |

## The first end-to-end journey (MVP milestone)
1. Student registers, picks **Kerala State → 2026-27 → Class 8 → Malayalam**.
2. Dashboard shows their real subjects and XP.
3. Opens Biology → Photosynthesis.
4. AI Teacher explains in Malayalam, grounded in approved chapter content, and says which passage it used.
5. Takes a 10-question quiz scored on the server.
6. Gets feedback on mistakes and a next step.
7. Earns XP through the ledger and sees it update live.

Steps 1, 2 and 4 have working code paths in this foundation; 3, 5, 6 need content and the exam module.

## Product principles
- **Accurate curriculum first.** Only official syllabus sources and licensed materials are the source of truth. Content is versioned per academic year and goes Draft → In review → Approved → Published. Nothing is hardcoded.
- **Honest AI.** Every answer is labelled as either *from your lessons* (with sources) or *general explanation, not from your textbook*. The AI never invents textbook references.
- **Reward learning, not clicking.** XP comes only from server-verified events (lesson completion, graded quizzes). Chatting with the AI earns nothing.
- **Child safety is a core feature.** Under-18 accounts need verified parental consent before AI features. No ads to children.
- **Real data only.** No fake dashboard numbers; empty states say what's missing.
- **Malayalam is first-class.** UI, AI replies and voice, including mixed Malayalam-English questions.

## Pilot recommendation
Kerala State, Classes 8–10, Malayalam and English, a small set of verified subjects and chapters. Expand only once the verified-content pipeline is working.

## Pricing hypothesis (to test, not validated)
Free ₹0 · Student ₹99/mo · Plus ₹199/mo · Entrance Pro ₹499/mo · School custom. Free plan has real value; AI features have disclosed fair-use limits (the foundation enforces 40 AI messages per student per day).
