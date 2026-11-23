import type { AttentionItem, InvestPlan } from '@astryx/api-client';

import { moneySentence, nextStep } from '../nextStep';

const item = (kind: string, title = kind): AttentionItem => ({ kind, title, body: '', deepLink: `/${kind}`, severity: 'info' });
const plan = (rebalanceSuggested: boolean) => ({ rebalanceSuggested }) as InvestPlan;

describe('nextStep (T-191)', () => {
  it.each([
    [{ goal: 'invest', attention: [item('pending_proposal'), item('halted')], plan: null, openPositions: 0 }, 'halted'],
    [{ goal: 'practice', attention: [item('pending_proposal'), item('stop_proximity')], plan: null, openPositions: 1 }, 'stop_proximity'],
    [{ goal: 'practice', attention: [item('pending_proposal')], plan: null, openPositions: 0 }, 'pending_proposal'],
    [{ goal: 'invest', attention: [], plan: null, openPositions: 2 }, 'start_plan'],
    [{ goal: 'practice', attention: [], plan: plan(true), openPositions: 1 }, 'rebalance'],
    [{ goal: 'practice', attention: [], plan: null, openPositions: 0 }, 'first_trade'],
    [{ goal: 'practice', attention: [item('working_order')], plan: null, openPositions: 0 }, 'all_good'],
    [{ goal: 'explore', attention: [], plan: null, openPositions: 0 }, 'explore'],
    [{ goal: 'invest', attention: [], plan: plan(false), openPositions: 0 }, 'all_good'],
    [{ goal: null, attention: [], plan: null, openPositions: 0 }, 'all_good'],
  ] as const)('%j → %s', (state, kind) => {
    expect(nextStep({ ...state, attention: [...state.attention] }).kind).toBe(kind);
  });
});

describe('moneySentence', () => {
  it.each([
    ['10250.00', '250.00', 'You have $10,250.00 of practice money, up $250.00 since you started.'],
    ['9800.00', '-200.00', 'You have $9,800.00 of practice money, down $200.00 since you started.'],
    ['10000.00', '0', 'You have $10,000.00 of practice money, the same as when you started.'],
  ])('%s, %s', (equity, pnl, expected) => expect(moneySentence(equity, pnl)).toBe(expected));
});
