import { ScannerStatus, Signal, SignalPage } from '@astryx/api-client';

// Payloads captured from the local stack after a scanner run (T-066).
const STATUS = { status: 'live', symbolsTotal: 1031, lastRunAt: '2026-11-12T15:00:14.217141Z', dataAsOf: '2026-11-12T15:00:00.490773Z', errorCode: null };
const DETAIL = {
  id: '01a1d045-f39e-7804-a3fd-bfd4e5ec9cb1',
  rank: 1,
  symbol: 'NVDA',
  name: 'NVIDIA Corp.',
  sector: 'Semiconductors',
  type: 'breakout',
  horizon: 'swing',
  price: '186.40',
  changePct: '0.0342',
  score: '87',
  metrics: { relVolume: '2.40', relStrength: '98', rewardRisk: '2.11', rsi14: '84.0' },
  reason: 'Cleared a 6-week base at $185.90 on 2.4× average volume.',
  levels: { orderType: 'buy_stop', entry: '186.50', stop: '178.20', target: '204.00' },
  strategy: { id: '480e94bb-67d3-eae2-7793-a62aa20742d4', name: 'Base Breakout v3', version: 3 },
  evaluatedAt: '2026-11-12T15:00:06.630973Z',
  dataAsOf: '2026-11-12T15:00:00.491065Z',
  status: 'active',
  scoreBreakdown: [
    { value: '0.8617', weight: '30', component: 'setup', contribution: '25.85' },
    { value: '0.99', weight: '20', component: 'relativeStrength', contribution: '19.80' },
    { value: '0.8', weight: '20', component: 'volume', contribution: '16.0' },
    { value: '1', weight: '15', component: 'trend', contribution: '15' },
    { value: '0.7027', weight: '15', component: 'rewardRisk', contribution: '10.54' },
  ],
  proposalId: null, sparkline: ['180.24', '186.40'], volume: 412800000, expiresAt: '2026-11-10T19:32:00Z',
};
const PAGE = {
  items: [
    {
      id: '01a1d045-f39e-7804-a3fd-bfd4e5ec9cb1',
      rank: 1,
      symbol: 'NVDA',
      name: 'NVIDIA Corp.',
      sector: 'Semiconductors',
      type: 'breakout',
      horizon: 'swing',
      price: '186.40',
      changePct: '0.0342',
      score: '87',
      metrics: { relVolume: '2.40', relStrength: '98', rewardRisk: '2.11', rsi14: '84.0' },
      reason: 'Cleared a 6-week base at $185.90 on 2.4× average volume.',
      levels: { orderType: 'buy_stop', entry: '186.50', stop: '178.20', target: '204.00' },
      strategy: { id: '480e94bb-67d3-eae2-7793-a62aa20742d4', name: 'Base Breakout v3', version: 3 },
      evaluatedAt: '2026-11-12T15:00:06.630973Z',
      dataAsOf: '2026-11-12T15:00:00.491065Z',
      status: 'active',
      scoreBreakdown: null,
      proposalId: null, sparkline: ['180.24', '186.40'], volume: 412800000, expiresAt: '2026-11-10T19:32:00Z',
    },
  ],
  nextCursor: null,
  counts: { all: 1, momentum: 0, breakout: 1, pullback: 0, unusualVolume: 0, earnings: 0 },
  scanner: { lastRunAt: '2026-11-12T15:00:14.217141Z', dataAsOf: '2026-11-12T15:00:00.490773Z' },
};

describe('scanner response schemas', () => {
  it('accepts the scanner status', () => {
    expect(ScannerStatus.parse(STATUS).status).toBe('live');
  });

  it('accepts a signal page and keeps levels as decimal strings', () => {
    const page = SignalPage.parse(PAGE);
    expect(page.items[0]!.levels).toEqual({ orderType: 'buy_stop', entry: '186.50', stop: '178.20', target: '204.00' });
    expect(page.items[0]!.scoreBreakdown).toBeNull();
    expect(page.counts.breakout).toBeGreaterThanOrEqual(1);
  });

  it('accepts a signal detail with its score breakdown', () => {
    const signal = Signal.parse(DETAIL);
    expect(signal.scoreBreakdown?.map((c) => c.component)).toEqual(['setup', 'relativeStrength', 'volume', 'trend', 'rewardRisk']);
    expect(signal.reason).toBe('Cleared a 6-week base at $185.90 on 2.4× average volume.');
  });

  it('rejects a level sent as a JSON number', () => {
    expect(() => Signal.parse({ ...DETAIL, levels: { ...DETAIL.levels, entry: 186.5 } })).toThrow();
  });
});
