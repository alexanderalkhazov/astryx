import type { Proposal, RiskAssessment } from '@astryx/api-client';
import Decimal from 'decimal.js-light';

import { formatMoney, formatPct } from '@/lib/format';

/** A proposal in everyday words (T-161): built from its numbers, never from AI, so it always matches the assessment. */
export interface PlainProposal {
  headline: string;
  outcomes: string[];
  why: string[];
  risks: string[];
}

/** Why each strategy's setups can work, without jargon. */
const WHY: Record<string, string> = {
  'Base Breakout':
    'The stock moved sideways in a tight range for several weeks and has just pushed above the top of it, on more trading than usual. Moves like this sometimes keep going.',
  'Trend Momentum': 'The stock has been rising steadily and is still near its highs. Strong trends often last longer than people expect.',
  'Trend Pullback': 'The stock is in a long rise and has just dipped back to a level where it found buyers before.',
  'RSI(2) Mean Reversion':
    'The stock is in a long rise but dropped sharply over the last couple of days. Short, sharp drops in strong stocks often bounce back.',
  'Earnings Drift':
    'The stock jumped after news, on heavy trading, and has held the jump. Stocks that hold a gap like this often keep drifting up.',
};

const strategyFamily = (name: string | null) => Object.keys(WHY).find((key) => name?.startsWith(key));

export function plainProposal(p: Proposal, a: RiskAssessment): PlainProposal {
  const qty = new Decimal(a.quantity).toFixed(0);
  const shares = `${qty} ${p.symbol} share${qty === '1' ? '' : 's'}`;
  const headline =
    p.orderType === 'buy_stop'
      ? `Buy ${shares} if the price rises to ${formatMoney(p.entry)} today.`
      : p.orderType === 'limit'
        ? `Buy ${shares} if the price dips to ${formatMoney(p.entry)} today.`
        : `Buy ${shares} at the market price, about ${formatMoney(a.quote.last)}.`;

  const lossShare = new Decimal(a.equity).isZero() ? null : new Decimal(a.maxLossInclCosts).div(a.equity).toString();
  const outcomes = [
    `If it falls to ${formatMoney(p.stop)}, it's sold automatically and you lose about ${formatMoney(a.maxLossInclCosts)}${lossShare ? ` (${formatPct(lossShare)} of your account)` : ''}.`,
    `If it reaches ${formatMoney(p.target)}, it's sold automatically and you gain about ${formatMoney(a.potentialProfit)}.`,
    `It uses ${formatMoney(a.capitalRequired)} of your ${formatMoney(a.cashAvailable)} available cash. Paper money only.`,
  ];

  const family = strategyFamily(p.source.strategyName);
  const why = family
    ? [WHY[family]!]
    : [p.analysis.summary || 'An AI research summary suggested this setup; the risk checks below are the same as for any trade.'];

  const risks = [
    'Many setups like this fail and fall back. The stop limits how much that costs.',
    'Prices can jump past the stop overnight or on news, so a loss can be larger than planned.',
    'Past patterns do not guarantee future results, and this is not investment advice.',
  ];
  return { headline, outcomes, why, risks };
}
