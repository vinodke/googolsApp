import { Link } from 'react-router'
import '../../app/layout.css'

/** Public privacy notice (linked from Google sign-in and the consent screen). Plain language; review with counsel before launch. */
export function PrivacyPage() {
  return (
    <div className="content" style={{ maxWidth: 820, margin: '0 auto', padding: 'var(--space-6) var(--space-4)' }}>
      <article className="card lesson-body" style={{ padding: 'var(--space-6)' }}>
        <p><Link to="/login">← Googols</Link></p>
        <h1>Privacy policy</h1>
        <p className="muted">Last updated 27 November 2026</p>

        <h3>Who we are</h3>
        <p>Googols is an online learning platform for school students in India (googols.in). Contact: wenothke@gmail.com.</p>

        <h3>What we collect</h3>
        <ul>
          <li><strong>Account:</strong> email address, and your name if you sign in with Google. We never receive your Google password.</li>
          <li><strong>Student profile:</strong> display name, date of birth, syllabus, class and preferred language.</li>
          <li><strong>Learning activity:</strong> lessons completed, quiz answers and scores, XP, badges and streaks.</li>
          <li><strong>AI teacher:</strong> questions you ask and the answers given, saved so you can return to a chat.</li>
          <li><strong>Parents and teachers:</strong> the link between a parent or teacher account and a student, and consent records.</li>
          <li><strong>Payments:</strong> plan and payment status. Card and UPI details are handled by Razorpay and never reach our servers.</li>
        </ul>

        <h3>How we use it</h3>
        <p>Only to teach, track progress, show progress to linked parents and teachers, keep the service safe and run subscriptions. We do not show advertising and we do not sell personal data.</p>

        <h3>Children</h3>
        <p>Students under 18 need a parent's or guardian's verifiable consent before AI features are switched on. Parents can withdraw consent at any time from the parent dashboard, which turns the AI teacher off for that child.</p>

        <h3>AI processing</h3>
        <p>AI teacher questions are answered by an AI model hosted in Microsoft Azure. Messages containing phone numbers or email addresses are blocked before they are sent, and answers are checked for safety.</p>

        <h3>Where data is stored</h3>
        <p>Data is stored in Microsoft Azure data centres. Access is limited to people who run the service.</p>

        <h3>Your choices</h3>
        <ul>
          <li>Students can hide themselves from the class leaderboard and leave a teacher's classroom.</li>
          <li>You can ask us to correct or delete your account and data by writing to the contact address above.</li>
        </ul>

        <h3>Changes</h3>
        <p>If this policy changes in a way that affects how children's data is used, parents will be asked to confirm consent again.</p>
      </article>
    </div>
  )
}
