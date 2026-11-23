import type { Proposal, RiskAssessment, Signal } from '@astryx/api-client';
import Decimal from 'decimal.js-light';

import { provenForBeginners } from '@/features/strategies/proven';
import { formatMoney, formatPct, formatPrice, formatQuantity } from '@/lib/format';
import { secureStorage } from '@/lib/secureStorage';

/** T-193: the guided first trade, one idea at a time. */
export const STEPS = ['intro', 'stock', 'plan', 'stop', 'risk', 'approve', 'done'] as const;
export type Step = (typeof STEPS)[number];

/** Where the user left off, per account on this device. */
export interface GuidedProgress {
  step: Step;
  proposalId?: string;
}

export async function loadProgress(userId: string): Promise<GuidedProgress> {
  const stored = await secureStorage.getGuidedTrade(userId);
  if (!stored) return { step: 'intro' };
  try {
    const parsed = JSON.parse(stored) as GuidedProgress;
    return STEPS.includes(parsed.step) ? parsed : { step: 'intro' };
  } catch {
    return { step: 'intro' };
  }
}

export const saveProgress = (userId: string, progress: GuidedProgress) => secureStorage.setGuidedTrade(userId, JSON.stringify(progress));

/** The idea to practise on: a proven strategy's when there is one, otherwise the highest-scoring setup. */
export function pickSetup(signals: readonly Signal[], exclude: readonly string[] = []): { signal: Signal; proven: boolean } | null {
  const open = signals.filter((s) => !exclude.includes(s.symbol));
  const proven = open.find(provenForBeginners);
  if (proven) return { signal: proven, proven: true };
  return open[0] ? { signal: open[0], proven: false } : null;
}

/** Plain words for each step, from the real proposal and the server's risk assessment. */
export function stepCopy(
  step: Exclude<Step, 'intro' | 'approve' | 'done'>,
  p: Proposal,
  a: RiskAssessment | null,
): { title: string; body: string[] } {
  switch (step) {
    case 'stock':
      return {
        title: `This is ${p.name}`,
        body: [
          `A share is a small piece of a company. ${p.symbol} is the short name it trades under.`,
          a ? `One share costs about ${formatPrice(a.quote.last)} right now.` : 'Its price moves during the day as people buy and sell.',
          `Our scanner picked it because: ${p.analysis.headline}`,
        ],
      };
    case 'plan':
      return {
        title: 'The plan',
        body:
          p.orderType === 'buy_stop'
            ? [
                `Buy only if the price rises to ${formatPrice(p.entry)}.`,
                'Rising past that level suggests the climb is continuing. If it doesn’t get there today, the order ends and nothing happens.',
              ]
            : p.orderType === 'limit'
              ? [
                  `Buy only if the price dips to ${formatPrice(p.entry)} or lower, so you never pay more than that.`,
                  'If it doesn’t get there today, nothing happens.',
                ]
              : [`Buy straight away at about ${formatPrice(p.entry)}.`],
      };
    case 'stop':
      return {
        title: 'The safety net',
        body: [
          `If the price falls to ${formatPrice(p.stop)} after you buy, the shares sell automatically. This is the stop: it caps how much you can lose.`,
          `If it rises to ${formatPrice(p.target)}, they sell too and you keep the gain. This is the target.`,
        ],
      };
    case 'risk': {
      if (!a) return { title: 'How much could you lose?', body: ['Checking the numbers with your risk limits…'] };
      const share = new Decimal(a.maxLoss).div(a.equity).toString();
      return {
        title: 'How much could you lose?',
        body: [
          `You’d buy ${formatQuantity(a.quantity)} shares for about ${formatMoney(a.capitalRequired)}.`,
          `If the price falls to the stop, you’d lose about ${formatMoney(a.maxLoss)}: ${formatPct(share)} of your ${formatMoney(a.equity, { dp: 0 })} practice money.`,
          `If it reaches the target, you’d gain about ${formatMoney(a.potentialProfit)}.`,
          'Astryx sized this for you from your risk limits, so one trade can only lose a small part of your money.',
        ],
      };
    }
  }
}
