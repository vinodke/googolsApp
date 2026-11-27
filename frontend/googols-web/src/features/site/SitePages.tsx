import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { Icon, type IconName } from '../../app/Icons'
import { api, errorMessage, fieldErrors } from '../../core/api/client'

// ---------- shared pieces

interface Feature { icon: IconName; tint: string; title: string; text: string }

const FEATURES: Feature[] = [
  { icon: 'sparkles', tint: 'tint-indigo', title: 'AI teacher, any time', text: 'Ask doubts in Malayalam or English and get patient, step-by-step explanations at your class level, with everyday examples from Kerala.' },
  { icon: 'book', tint: 'tint-teal', title: 'Your syllabus, your class', text: 'Lessons follow your board, academic year, class and subject, so you learn exactly what your school teaches.' },
  { icon: 'shield', tint: 'tint-sky', title: 'Answers from your lessons', text: 'The AI teacher answers from approved lesson text first and clearly marks when an answer is only a general explanation.' },
  { icon: 'bolt', tint: 'tint-amber', title: 'Chapter quizzes', text: 'Short quizzes after each chapter, marked instantly with explanations so mistakes turn into learning.' },
  { icon: 'trophy', tint: 'tint-rose', title: 'XP, badges and streaks', text: 'Earn XP for real learning, level up, keep a daily streak and collect badges. Learning feels like a game.' },
  { icon: 'grad', tint: 'tint-lime', title: 'Parents and teachers see progress', text: 'Parents follow progress and control consent. Teachers create classrooms and see which chapters need attention.' },
]

function Section({ id, eyebrow, title, intro, children, tone }: { id?: string; eyebrow?: string; title: string; intro?: ReactNode; children: ReactNode; tone?: 'soft' }) {
  return (
    <section id={id} className={`site-section${tone === 'soft' ? ' soft' : ''}`}>
      <div className="site-container">
        <header className="section-head">
          {eyebrow && <span className="eyebrow">{eyebrow}</span>}
          <h2>{title}</h2>
          {intro && <p className="muted">{intro}</p>}
        </header>
        {children}
      </div>
    </section>
  )
}

function FeatureGrid({ items }: { items: Feature[] }) {
  return (
    <div className="feature-grid">
      {items.map(f => (
        <article key={f.title} className="card feature">
          <span className={`icon-tile lg ${f.tint}`}><Icon name={f.icon} /></span>
          <h3>{f.title}</h3>
          <p className="muted">{f.text}</p>
        </article>
      ))}
    </div>
  )
}

function Steps({ steps }: { steps: { title: string; text: string }[] }) {
  return (
    <ol className="steps-row">
      {steps.map((s, i) => (
        <li key={s.title} className="card step">
          <span className="step-no">{i + 1}</span>
          <h3>{s.title}</h3>
          <p className="muted small">{s.text}</p>
        </li>
      ))}
    </ol>
  )
}

function CtaBand() {
  return (
    <section className="site-section">
      <div className="site-container">
        <div className="cta-band">
          <div>
            <h2>Start learning with Googols today</h2>
            <p>Free to start. Set up your syllabus and class in a minute.</p>
          </div>
          <div className="row">
            <Link to="/register" className="btn btn-light">Sign up free</Link>
            <Link to="/how-it-works" className="btn btn-glass">See how it works</Link>
          </div>
        </div>
      </div>
    </section>
  )
}

const STUDENT_STEPS = [
  { title: 'Sign up', text: 'Create a free account with your email or Google.' },
  { title: 'Pick your class', text: 'Choose your board, academic year, class and learning language.' },
  { title: 'Learn and ask', text: 'Read lessons and ask the AI teacher whenever you are stuck.' },
  { title: 'Practise and grow', text: 'Take chapter quizzes, earn XP and badges, and keep your streak.' },
]

// ---------- Home

export function HomePage() {
  return (
    <>
      <section className="hero">
        <div className="site-container hero-grid">
          <div className="hero-copy">
            <span className="eyebrow"><Icon name="sparkles" size={14} /> AI learning for Kerala students</span>
            <h1>Learn smarter with your own <span className="grad-text">AI teacher</span></h1>
            <p className="lead">Googols explains every chapter of your syllabus in Malayalam or English, answers doubts any time, and makes practice fun with quizzes, XP and badges.</p>
            <div className="row">
              <Link to="/register" className="btn btn-primary">Sign up free <Icon name="chevron" size={18} /></Link>
              <Link to="/how-it-works" className="btn btn-ghost">How it works</Link>
            </div>
            <ul className="hero-points">
              <li><Icon name="check" size={16} /> Follows your syllabus and class</li>
              <li><Icon name="check" size={16} /> മലയാളം &amp; English</li>
              <li><Icon name="check" size={16} /> No ads, parent-approved</li>
            </ul>
          </div>
          <HeroMock />
        </div>
      </section>

      <section className="trust">
        <div className="site-container trust-row">
          <span><Icon name="book" size={18} /> Kerala SCERT syllabus</span>
          <span><Icon name="globe" size={18} /> Malayalam and English</span>
          <span><Icon name="shield" size={18} /> Parental consent built in</span>
          <span><Icon name="star" size={18} /> Free plan available</span>
        </div>
      </section>

      <Section eyebrow="Features" title="Everything a student needs to learn well" intro="One place to read lessons, clear doubts, practise and track progress.">
        <FeatureGrid items={FEATURES} />
        <p style={{ textAlign: 'center', marginTop: 'var(--space-5)' }}><Link to="/features" className="btn btn-ghost">See all features <Icon name="chevron" size={16} /></Link></p>
      </Section>

      <Section eyebrow="How it works" title="Start in four simple steps" tone="soft">
        <Steps steps={STUDENT_STEPS} />
      </Section>

      <Section eyebrow="For everyone" title="Built for students, parents and teachers">
        <div className="audience-grid">
          <Audience icon="grad" tint="tint-indigo" title="Students" points={['Lessons for your exact class', 'AI teacher for doubts, day or night', 'Quizzes, XP, badges and streaks']} />
          <Audience icon="shield" tint="tint-teal" title="Parents" points={['Link your child with a one-time code', 'Give or withdraw consent any time', 'See lessons done, streaks and rewards']} />
          <Audience icon="layers" tint="tint-amber" title="Teachers" points={['Create classrooms with a join code', 'See each student\'s progress', 'Spot the chapters that need attention']} />
        </div>
      </Section>

      <Section eyebrow="Safety first" title="A safe place for children to learn" tone="soft">
        <div className="safety-grid">
          {[
            ['Parental consent', 'Under-18 students use the AI teacher only after a parent agrees.'],
            ['Private by design', 'Phone numbers and email addresses are blocked from AI chats, and classmates only see a first name.'],
            ['Careful answers', 'Every answer is screened, and anyone who mentions self-harm gets a caring reply with a helpline number.'],
            ['No ads, ever', 'We never show advertising and never sell personal data.'],
          ].map(([t, d]) => (
            <div key={t} className="safety-item"><span className="icon-tile tint-teal"><Icon name="check" /></span><div><strong>{t}</strong><p className="muted small">{d}</p></div></div>
          ))}
        </div>
      </Section>

      <Section eyebrow="Questions" title="Frequently asked questions">
        <div className="faq">
          {[
            ['Is Googols free?', 'Yes. You can learn, take quizzes and use the AI teacher for free every day. Premium adds more AI teacher questions per day.'],
            ['Which syllabus does it follow?', 'Googols is built around the Kerala State (SCERT) syllabus, organised by academic year, class and subject. More boards can be added.'],
            ['Can I learn in Malayalam?', 'Yes. Choose Malayalam or English, and switch at any time. The AI teacher replies in the language you pick.'],
            ['Does the AI teacher make things up?', 'It answers from approved lesson text first and shows the lesson it used. When a question goes beyond the lessons, the answer is clearly labelled as a general explanation.'],
            ['How do parents give consent?', 'The student creates a one-time code on the Family page. The parent signs in with their own account, enters the code and accepts the consent notice.'],
          ].map(([q, a]) => (
            <details key={q} className="card faq-item"><summary>{q}</summary><p className="muted">{a}</p></details>
          ))}
        </div>
      </Section>

      <CtaBand />
    </>
  )
}

/** Illustration of the app built from real UI pieces (no screenshots or stock images). */
function HeroMock() {
  return (
    <div className="hero-mock" aria-hidden="true">
      <div className="mock-card mock-main">
        <div className="row-between">
          <div className="row"><span className="teacher-dot"><Icon name="sparkles" size={18} /></span><strong>AI Teacher</strong></div>
          <span className="badge badge-success">Online</span>
        </div>
        <div className="mock-bubble user">What is photosynthesis?</div>
        <div className="mock-bubble ai">
          Green plants make their own food using <strong>sunlight</strong>, water and carbon dioxide. Chlorophyll in the leaves traps the light, and oxygen is released. 🌿
          <span className="badge badge-success" style={{ marginTop: 8 }}><Icon name="book" size={12} /> From your lessons</span>
        </div>
        <div className="mock-bubble user">ലളിതമായി പറയൂ</div>
      </div>
      <div className="mock-card mock-xp"><Icon name="star" size={18} /> <strong>+45 XP</strong><span className="small muted">Quiz: 100%</span></div>
      <div className="mock-card mock-streak"><Icon name="flame" size={18} /> <strong>7-day streak</strong></div>
    </div>
  )
}

function Audience({ icon, tint, title, points }: { icon: IconName; tint: string; title: string; points: string[] }) {
  return (
    <article className="card audience">
      <span className={`icon-tile lg ${tint}`}><Icon name={icon} /></span>
      <h3>{title}</h3>
      <ul>{points.map(p => <li key={p}><Icon name="check" size={16} /> {p}</li>)}</ul>
    </article>
  )
}

// ---------- Features

export function FeaturesPage() {
  const detail: { icon: IconName; tint: string; title: string; text: string; points: string[] }[] = [
    { icon: 'sparkles', tint: 'tint-indigo', title: 'AI teacher', text: 'A patient tutor that never gets tired of questions.', points: ['Explains step by step at your class level', 'Malayalam or English replies', 'Quick buttons: explain simply, give an example, test me', 'Chats are saved so you can continue later'] },
    { icon: 'book', tint: 'tint-teal', title: 'Lessons by syllabus', text: 'Clear lessons arranged by subject and chapter.', points: ['Board → year → class → subject → chapter', 'Textbook pages or diagrams to download', 'Mark lessons complete and track progress', 'Shows which lessons are from the textbook and which are teacher notes'] },
    { icon: 'bolt', tint: 'tint-amber', title: 'Quizzes', text: 'Practise every chapter and learn from mistakes.', points: ['Up to 10 questions per quiz', 'Instant marks with explanations', 'Retake to improve your best score', 'Bonus XP for 90% and above'] },
    { icon: 'trophy', tint: 'tint-rose', title: 'Rewards', text: 'Motivation that comes from real learning.', points: ['XP for lessons, chapters and quizzes', 'Levels and daily streaks', 'Badges such as Bookworm and Full marks', 'Class leaderboard (students can hide themselves)'] },
    { icon: 'shield', tint: 'tint-sky', title: 'For parents', text: 'Stay involved without hovering.', points: ['Link with a one-time code from your child', 'Give or withdraw consent for AI features', 'See level, XP, streak and recent rewards', 'Choose a plan for your child'] },
    { icon: 'grad', tint: 'tint-lime', title: 'For teachers', text: 'Know where your class needs help.', points: ['Classrooms with a join code', 'Lessons and quiz averages per student', 'Chapters with the lowest scores highlighted', 'Remove students or close joining any time'] },
  ]
  return (
    <>
      <PageHero eyebrow="Features" title="Everything in Googols" text="Built around how school students actually learn: read, ask, practise and see progress." />
      <section className="site-section">
        <div className="site-container feature-detail">
          {detail.map(d => (
            <article key={d.title} className="card feature-row">
              <span className={`icon-tile lg ${d.tint}`}><Icon name={d.icon} /></span>
              <div>
                <h3>{d.title}</h3>
                <p className="muted">{d.text}</p>
                <ul className="check-list">{d.points.map(p => <li key={p}><Icon name="check" size={16} /> {p}</li>)}</ul>
              </div>
            </article>
          ))}
        </div>
      </section>
      <CtaBand />
    </>
  )
}

// ---------- How it works

export function HowItWorksPage() {
  return (
    <>
      <PageHero eyebrow="How it works" title="Simple for students, parents and teachers" text="Here is what each person does, start to finish." />
      <Section eyebrow="Students" title="Learn in four steps">
        <Steps steps={STUDENT_STEPS} />
      </Section>
      <Section eyebrow="Parents" title="Stay in control" tone="soft">
        <Steps steps={[
          { title: 'Get the code', text: 'Your child opens Family in Googols and taps Create code.' },
          { title: 'Link your account', text: 'Sign up as yourself, open Link a child and enter the code.' },
          { title: 'Give consent', text: 'Read the short notice and agree. The AI teacher switches on for your child.' },
          { title: 'Follow progress', text: 'See lessons, streaks and rewards. Withdraw consent any time.' },
        ]} />
      </Section>
      <Section eyebrow="Teachers" title="Bring your class">
        <Steps steps={[
          { title: 'Get teacher access', text: 'Contact us and we enable teacher access on your account.' },
          { title: 'Create a classroom', text: 'Pick the class and get a six-letter join code.' },
          { title: 'Students join', text: 'Students enter the code on their Family page.' },
          { title: 'See insights', text: 'Track lessons, quiz averages and the chapters that need attention.' },
        ]} />
      </Section>
      <Section eyebrow="Behind the scenes" title="How the AI teacher answers" tone="soft">
        <div className="flow">
          {[
            ['1', 'You ask', 'Your question, your class, subject and chapter.'],
            ['2', 'Safety check', 'Personal details and unsafe content are screened.'],
            ['3', 'Find your lesson', 'Googols looks up the matching approved lesson text.'],
            ['4', 'Teach', 'The AI explains at your level and shows the lesson it used.'],
          ].map(([n, t, d]) => (
            <div key={n} className="flow-item"><span className="step-no">{n}</span><strong>{t}</strong><span className="muted small">{d}</span></div>
          ))}
        </div>
      </Section>
      <CtaBand />
    </>
  )
}

// ---------- About

export function AboutPage() {
  return (
    <>
      <PageHero eyebrow="About us" title="Every child deserves a patient teacher" text="Googols is a learning platform for school students in Kerala, built to make good explanations available to every child, in their own language, at any hour." />
      <Section title="What we are">
        <div className="about-grid">
          <div className="card">
            <h3>Our mission</h3>
            <p className="muted">Help every student understand their lessons, not just memorise them, by pairing their own syllabus with an AI teacher that explains, checks understanding and encourages.</p>
          </div>
          <div className="card">
            <h3>What makes us different</h3>
            <p className="muted">We start from the student's actual syllabus and class. Answers come from approved lesson text first, and every answer shows whether it came from the lessons or is a general explanation.</p>
          </div>
          <div className="card">
            <h3>Who it's for</h3>
            <p className="muted">Students from Class 1 to 12 and entrance-exam learners, the parents who support them, and the teachers and schools who guide them.</p>
          </div>
        </div>
      </Section>
      <Section title="What we believe" tone="soft">
        <div className="values">
          {([
            ['globe', 'Learn in your language', 'Malayalam and English side by side, so language is never a barrier to understanding.'],
            ['shield', 'Safety and privacy first', 'Parental consent, no ads, no selling data, and careful screening of every AI answer.'],
            ['check', 'Honest AI', 'We show where answers come from and never pass off AI text as a textbook quote.'],
            ['trophy', 'Joy in learning', 'Progress, streaks and badges that reward real effort, not screen time.'],
          ] as [IconName, string, string][]).map(([icon, t, d]) => (
            <div key={t} className="value"><span className="icon-tile lg tint-indigo"><Icon name={icon} /></span><div><h3>{t}</h3><p className="muted small">{d}</p></div></div>
          ))}
        </div>
      </Section>
      <CtaBand />
    </>
  )
}

// ---------- Contact

export function ContactPage() {
  const [form, setForm] = useState({ name: '', email: '', phone: '', topic: 'general', message: '', website: '' })
  const send = useMutation({ mutationFn: () => api.post('/contact', form) })
  const errs = fieldErrors(send.error)
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm(f => ({ ...f, [k]: e.target.value }))
  function submit(e: FormEvent) {
    e.preventDefault()
    send.mutate()
  }
  return (
    <>
      <PageHero eyebrow="Contact us" title="We'd love to hear from you" text="Questions about Googols, bringing it to your school, or need help with your account? Send us a message." />
      <section className="site-section">
        <div className="site-container contact-grid">
          <div className="stack">
            <div className="card contact-item"><span className="icon-tile lg tint-indigo"><Icon name="send" /></span><div><strong>Email</strong><a href="mailto:wenothke@gmail.com">wenothke@gmail.com</a></div></div>
            <div className="card contact-item"><span className="icon-tile lg tint-teal"><Icon name="grad" /></span><div><strong>Schools and teachers</strong><span className="muted small">Choose "School or teacher" in the form and tell us about your school.</span></div></div>
            <div className="card contact-item"><span className="icon-tile lg tint-amber"><Icon name="shield" /></span><div><strong>Parents</strong><span className="muted small">Ask about consent, privacy or your child's account.</span></div></div>
          </div>
          <form className="card" onSubmit={submit} noValidate>
            {send.isSuccess ? (
              <div className="empty">
                <span className="icon-tile lg tint-lime"><Icon name="check" /></span>
                <h2>Thank you!</h2>
                <p className="muted">Your message has reached us. We'll reply to {form.email}.</p>
              </div>
            ) : (
              <>
                <h2>Send a message</h2>
                {send.isError && !Object.keys(errs).length && <div className="alert" role="alert">{errorMessage(send.error)}</div>}
                <div className="grid-2-even">
                  <div className="field"><label htmlFor="c-name">Your name</label><input id="c-name" className="input" value={form.name} onChange={set('name')} maxLength={100} />{errs.name && <span className="field-error">{errs.name}</span>}</div>
                  <div className="field"><label htmlFor="c-email">Email</label><input id="c-email" className="input" type="email" value={form.email} onChange={set('email')} maxLength={256} />{errs.email && <span className="field-error">{errs.email}</span>}</div>
                  <div className="field"><label htmlFor="c-phone">Phone (optional)</label><input id="c-phone" className="input" type="tel" value={form.phone} onChange={set('phone')} maxLength={20} /></div>
                  <div className="field"><label htmlFor="c-topic">Topic</label>
                    <select id="c-topic" className="input" value={form.topic} onChange={set('topic')}>
                      <option value="general">General question</option>
                      <option value="school">School or teacher</option>
                      <option value="parent">Parent</option>
                      <option value="support">Help with my account</option>
                      <option value="partnership">Partnership</option>
                    </select>
                  </div>
                </div>
                <div className="field"><label htmlFor="c-msg">Message</label><textarea id="c-msg" className="input" rows={6} value={form.message} onChange={set('message')} maxLength={2000} />{errs.message && <span className="field-error">{errs.message}</span>}</div>
                <div className="hp" aria-hidden="true"><label htmlFor="c-web">Website</label><input id="c-web" tabIndex={-1} autoComplete="off" value={form.website} onChange={set('website')} /></div>
                <button className="btn btn-primary" disabled={send.isPending}><Icon name="send" size={18} /> {send.isPending ? 'Sending…' : 'Send message'}</button>
                <p className="hint" style={{ marginTop: 'var(--space-3)' }}>We use your details only to reply. See our <Link to="/privacy">privacy policy</Link>.</p>
              </>
            )}
          </form>
        </div>
      </section>
    </>
  )
}

function PageHero({ eyebrow, title, text }: { eyebrow: string; title: string; text: string }) {
  return (
    <section className="page-hero">
      <div className="site-container">
        <span className="eyebrow">{eyebrow}</span>
        <h1>{title}</h1>
        <p className="lead">{text}</p>
      </div>
    </section>
  )
}
