import type { Proposal, RiskAssessment } from '@astryx/api-client';

import { plainProposal } from '../plainProposal';

const assessment = {
  quantity: '76',
  quote: { last: '186.40', asOf: '' },
  equity: '118181.30',
  cashAvailable: '21840.55',
  capitalRequired: '14174.00',
  maxLossInclCosts: '633.84',
  potentialProfit: '1330.00',
} as RiskAssessment;

const proposal = (over: Partial<Proposal> = {}) =>
  ({
    symbol: 'NVDA',
    orderType: 'buy_stop',
    entry: '186.50',
    stop: '178.20',
    target: '204.00',
    source: { type: 'signal', strategyName: 'Base Breakout v3' },
    analysis: { summary: 'AI summary text.' },
    ...over,
  }) as Proposal;

describe('plain-language proposal (T-161)', () => {
  it('says what is bought, what it can cost or make, and how much cash it uses, from the assessment numbers', () => {
    const plain = plainProposal(proposal(), assessment);

    expect(plain.headline).toBe('Buy 76 NVDA shares if the price rises to $186.50 today.');
    expect(plain.outcomes).toEqual([
      "If it falls to $178.20, it's sold automatically and you lose about $633.84 (0.54% of your account).",
      "If it reaches $204.00, it's sold automatically and you gain about $1,330.00.",
      'It uses $14,174.00 of your $21,840.55 available cash. Paper money only.',
    ]);
    expect(plain.why[0]).toMatch(/^The stock moved sideways in a tight range/);
    expect(plain.risks).toHaveLength(3);
  });

  it('words limit and market entries for what they are', () => {
    expect(plainProposal(proposal({ orderType: 'limit', entry: '180.00' }), assessment).headline).toBe(
      'Buy 76 NVDA shares if the price dips to $180.00 today.',
    );
    expect(plainProposal(proposal({ orderType: 'market' }), assessment).headline).toBe(
      'Buy 76 NVDA shares at the market price, about $186.40.',
    );
    expect(plainProposal(proposal(), { ...assessment, quantity: '1' }).headline).toBe(
      'Buy 1 NVDA share if the price rises to $186.50 today.',
    );
  });

  it("falls back to the analysis summary when the setup isn't from a known strategy", () => {
    const plain = plainProposal(proposal({ source: { type: 'analysis', strategyName: null } as Proposal['source'] }), assessment);

    expect(plain.why).toEqual(['AI summary text.']);
  });
});
