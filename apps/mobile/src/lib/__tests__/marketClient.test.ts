import { Bars, Breadth, MarketSession, Quote } from '@astryx/api-client';

// Payloads as the API sends them (SYSTEM_DESIGN.md §11.4), captured from the local stack.
describe('market response schemas', () => {
  it('accepts the design quote and keeps money as strings', () => {
    const quote = Quote.parse({
      symbol: 'NVDA', last: '186.40', change: '6.16', changePct: '0.0342', open: '180.60', high: '186.77', low: '180.05',
      prevClose: '180.24', bid: '186.38', ask: '186.42', volume: 412800000, asOf: '2026-11-11T21:00:00Z',
      quality: 'synthetic', stale: false, source: 'fake',
    });

    expect(typeof quote.last).toBe('string');
    expect(quote.changePct).toBe('0.0342');
  });

  it('rejects money sent as a JSON number', () => {
    expect(() => Quote.parse({ symbol: 'NVDA', last: 186.4 })).toThrow();
  });

  it('accepts bars where only the forming bar carries final', () => {
    const bars = Bars.parse({
      symbol: 'NVDA', interval: '1d', adjusted: false, asOf: '2026-11-09T19:32:00Z', stale: false, levels: null,
      bars: [
        { t: '2026-11-06T14:30:00Z', d: '2026-11-06', o: '178.10', h: '181.40', l: '177.20', c: '180.24', v: 301223000 },
        { t: '2026-11-09T14:30:00Z', d: '2026-11-09', o: '180.60', h: '186.77', l: '180.05', c: '186.40', v: 412800000, final: false },
      ],
    });

    expect(bars.bars[0]!.final).toBeUndefined();
    expect(bars.bars[1]!.final).toBe(false);
  });

  it('accepts breadth and session payloads', () => {
    expect(Breadth.parse({ advancers: 526, decliners: 501, pctAbove50dma: '0.4768', sentiment: 'neutral', asOf: '2026-11-11T21:00:00Z', stale: false }).sentiment).toBe('neutral');
    expect(
      MarketSession.parse({ exchange: 'XNYS', state: 'pre', sessionDate: '2026-11-12', opensAt: '2026-11-12T14:30:00Z', closesAt: '2026-11-12T21:00:00Z', isHalfDay: false }).state,
    ).toBe('pre');
  });
});
