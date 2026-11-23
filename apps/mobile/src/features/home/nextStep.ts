import type { AttentionItem, InvestPlan } from '@astryx/api-client';
import Decimal from 'decimal.js-light';
import type { Href } from 'expo-router';

import type { Goal } from '@/features/onboarding/goal';
import { formatMoney } from '@/lib/format';

/** T-191: the single most useful thing to do now, in plain words. */
export interface NextStep {
  kind: string;
  title: string;
  body: string;
  action?: { label: string; href: Href };
  tone: 'urgent' | 'normal' | 'calm';
}

export interface HomeState {
  goal: Goal | null | undefined;
  attention: readonly AttentionItem[];
  /** null: no investing plan yet. */
  plan: InvestPlan | null;
  openPositions: number;
}

/** Rules in order: anything blocking or at risk first, then the user's goal, then "all good". */
export function nextStep({ goal, attention, plan, openPositions }: HomeState): NextStep {
  const first = (kind: string) => attention.find((a) => a.kind === kind);

  const halted = first('halted');
  if (halted)
    return {
      kind: 'halted',
      title: halted.title,
      body: halted.body,
      tone: 'urgent',
      action: { label: 'See why', href: halted.deepLink as Href },
    };

  const nearStop = first('stop_proximity');
  if (nearStop)
    return {
      kind: 'stop_proximity',
      title: nearStop.title,
      body: 'Its price is close to the level where it sells automatically to limit your loss. Have a look at what changed.',
      tone: 'urgent',
      action: { label: 'Look at it', href: nearStop.deepLink as Href },
    };

  const proposal = first('pending_proposal');
  if (proposal)
    return {
      kind: 'pending_proposal',
      title: proposal.title,
      body: 'A trade idea is waiting for your yes or no. Nothing happens unless you approve it.',
      tone: 'normal',
      action: { label: 'Review it', href: proposal.deepLink as Href },
    };

  if (goal === 'invest' && plan === null)
    return {
      kind: 'start_plan',
      title: 'Start your investing plan',
      body: 'Pick a mix of three broad funds and how much to add each week or month. It takes about a minute.',
      tone: 'normal',
      action: { label: 'Start my plan', href: '/invest' },
    };

  if (plan?.rebalanceSuggested)
    return {
      kind: 'rebalance',
      title: 'Your plan has drifted',
      body: 'Prices moved, so your funds are no longer in the mix you chose. Rebalancing moves them back.',
      tone: 'normal',
      action: { label: 'See my plan', href: '/invest' },
    };

  if (goal === 'practice' && openPositions === 0 && !first('working_order'))
    return {
      kind: 'first_trade',
      title: 'Make your first practice trade',
      body: 'We’ll walk you through one of today’s ideas step by step, with pretend money.',
      tone: 'normal',
      action: { label: 'Start the guided trade', href: '/guided-trade' },
    };

  if (goal === 'explore' && openPositions === 0 && plan === null)
    return {
      kind: 'explore',
      title: 'Have a look around',
      body: 'Browse today’s ideas or set up a long-term plan when you’re ready. Nothing here costs real money.',
      tone: 'normal',
      action: { label: 'See today’s ideas', href: '/discover' },
    };

  return { kind: 'all_good', title: 'All good', body: 'Nothing needs you today. Check back tomorrow.', tone: 'calm' };
}

/** "You have $10,250.00 of practice money, up $250.00 since you started." */
export function moneySentence(equity: string, allTimePnl: string): string {
  const pnl = new Decimal(allTimePnl);
  const change = pnl.isZero()
    ? 'the same as when you started'
    : `${pnl.isNegative() ? 'down' : 'up'} ${formatMoney(pnl.abs().toString())} since you started`;
  return `You have ${formatMoney(equity)} of practice money, ${change}.`;
}
