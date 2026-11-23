/**
 * T-197: the first line of each screen in Simple mode, answering "what is this and what can I do here?". One place, so
 * the copy reads as one voice; `screenIntros.test.ts` fails when a signed-in screen has none.
 */
export const INTRO = {
  home: 'Your money at a glance, and the one thing worth doing next.',
  invest: 'Your long-term plan: a mix of broad funds that grows with regular top-ups.',
  practice: 'Try trading single stocks with pretend money. Your trades and today’s ideas are here.',
  learn: 'Short, plain explanations of the words Astryx uses. Tap one to read it.',
  discover: 'Today’s trade ideas from the scanner. Tap one to see the stock and its plan.',
  portfolio: 'What you own right now, how it’s spread out and how it has done.',
  strategies: 'The rules behind the trade ideas, and how each did on past prices and live.',
  activity: 'Everything that happened in your account: orders, fills and alerts. Tap one for details.',
  stock: 'One stock: its price, today’s idea if there is one, and AI research about it.',
  proposal: 'A trade idea waiting for your decision. Nothing is bought unless you approve it.',
  position: 'A stock you own: what it’s worth, and where it sells automatically to limit a loss or take a profit.',
  strategy: 'One strategy: its rules and how it did on past prices and since it went live.',
  search: 'Find any stock by its ticker or company name.',
  settings: 'Your goal, experience level, limits and account. Changes apply straight away.',
  watchlist: 'Stocks you’re keeping an eye on. Add one with the star on any stock’s page.',
  changePassword: 'Choose a new password for signing in.',
  connectAlpaca: 'Link an Alpaca paper account, so practice trades run at a real broker with pretend money.',
} as const;
