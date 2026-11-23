/** One-paragraph explanations for the trading words the app uses (T-161). Plain language, no further jargon. */
export const GLOSSARY = {
  breakout: {
    term: 'Breakout',
    body: 'A stock that traded in a narrow range for weeks and then rises above the top of that range. Sometimes the move keeps going; often it falls back, which is why every trade has a stop.',
  },
  buyStop: {
    term: 'Buy stop order',
    body: 'An order to buy only if the price rises to a set level. If the stock never gets there today, nothing is bought and the order lapses at the close.',
  },
  stop: {
    term: 'Stop loss',
    body: 'A price below what you paid where the position is sold automatically to cap the loss. If the price jumps below it overnight, the sale happens at the next available price, which can be lower.',
  },
  target: {
    term: 'Target',
    body: 'A price above what you paid where the position is sold automatically to take the profit.',
  },
  rewardRisk: {
    term: 'Reward to risk (R:R)',
    body: 'How much you stand to gain at the target for every $1 you could lose at the stop. 2.0 means $2 of possible gain per $1 of possible loss. It says nothing about how likely either is.',
  },
  positionSize: {
    term: 'Position size',
    body: 'How many shares to buy. Astryx works it out so that hitting the stop loses only a small, fixed share of your account, and no single stock becomes too large a part of it.',
  },
  riskPerTrade: {
    term: 'Risk per trade',
    body: 'The share of your account you would lose if the stop is hit, for example 0.75 %. Keeping it small means a run of losing trades does limited damage.',
  },
  atr: {
    term: 'ATR (average true range)',
    body: "How much the stock's price typically moves in a day. Stops are set a few of these away so normal daily swings don't trigger them.",
  },
  rsi: {
    term: 'RSI',
    body: 'A 0–100 gauge of recent price moves: above 70 means it rose a lot recently, below 30 that it fell a lot. It describes the past; it does not predict.',
  },
  relativeStrength: {
    term: 'Relative strength',
    body: 'How this stock performed over the past year compared with the other stocks Astryx follows, from 1 (weakest) to 99 (strongest).',
  },
  volume: {
    term: 'Volume',
    body: 'How many shares changed hands. A breakout on much higher volume than usual means more buyers took part.',
  },
  sectorExposure: {
    term: 'Sector exposure',
    body: 'How much of your account is in one industry, such as technology. Spreading across industries means one bad sector hurts less.',
  },
  paper: {
    term: 'Paper trading',
    body: 'Practice trading with simulated money. Orders behave like real ones, but no real money is ever at risk.',
  },
} as const;

export type GlossaryKey = keyof typeof GLOSSARY;
