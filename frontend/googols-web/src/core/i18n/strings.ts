import type { Lang } from '../api/types'

// UI strings. Learning content language is chosen separately per student; this is the interface chrome.
// Malayalam strings must be reviewed by a native speaker before launch.
const en = {
  appTagline: 'Learn Smarter. Grow Further. Achieve Anything.',
  navHome: 'Home',
  navSubjects: 'Subjects',
  navAiTeacher: 'AI Teacher',
  navProgress: 'My Progress',
  signIn: 'Sign in',
  signOut: 'Sign out',
  createAccount: 'Create account',
  email: 'Email',
  password: 'Password',
  welcome: (name: string) => `Welcome back, ${name}!`,
  level: 'Level',
  xp: 'XP',
  streak: (n: number) => `${n}-day streak`,
  continueLearning: 'Continue learning',
  askTeacher: 'Ask your AI teacher',
  mySubjects: 'My subjects',
  recentXp: 'Recent rewards',
  noXpYet: 'Finish a lesson or quiz to earn your first XP.',
  noSubjects: 'No subjects are published for your class yet.',
  explainSimply: 'Explain simply',
  giveExample: 'Give me an example',
  testMe: 'Test my knowledge',
  typeQuestion: 'Type your question…',
  send: 'Send',
  sourcesUsed: 'From your lessons',
  generalExplanation: 'General explanation: not from your textbook',
  remaining: (n: number) => `${n} questions left today`,
  consentNeeded: 'A parent needs to give permission before you can use the AI teacher.',
}

type Strings = typeof en

const ml: Strings = {
  ...en,
  navHome: 'ഹോം',
  navSubjects: 'വിഷയങ്ങൾ',
  navAiTeacher: 'AI ടീച്ചർ',
  navProgress: 'എന്റെ പുരോഗതി',
  signIn: 'സൈൻ ഇൻ',
  signOut: 'സൈൻ ഔട്ട്',
  welcome: (name: string) => `സ്വാഗതം, ${name}!`,
  level: 'ലെവൽ',
  streak: (n: number) => `${n} ദിവസത്തെ തുടർച്ച`,
  continueLearning: 'പഠനം തുടരുക',
  askTeacher: 'AI ടീച്ചറോട് ചോദിക്കൂ',
  mySubjects: 'എന്റെ വിഷയങ്ങൾ',
  explainSimply: 'ലളിതമായി പറയൂ',
  giveExample: 'ഒരു ഉദാഹരണം തരൂ',
  testMe: 'എന്നെ പരീക്ഷിക്കൂ',
  typeQuestion: 'നിങ്ങളുടെ ചോദ്യം ടൈപ്പ് ചെയ്യൂ…',
  send: 'അയക്കുക',
  sourcesUsed: 'നിങ്ങളുടെ പാഠങ്ങളിൽ നിന്ന്',
  generalExplanation: 'പൊതുവായ വിശദീകരണം: പാഠപുസ്തകത്തിൽ നിന്നല്ല',
}

export const strings: Record<Lang, Strings> = { en, ml }
