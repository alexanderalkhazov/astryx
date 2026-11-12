import { bodyWidth, indexAt, priceRange, sma, spreadTags, toPlotBars, xFor, yFor } from '../geometry';

const bar = (o: string, h: string, l: string, c: string, v = 100) => ({ t: '2026-11-09T14:30:00Z', d: '2026-11-09', o, h, l, c, v });

describe('chart geometry', () => {
  it('converts strings to numbers and marks direction', () => {
    expect(toPlotBars([bar('180.60', '186.77', '180.05', '186.40'), bar('10', '11', '9', '9.5')])).toEqual([
      { o: 180.6, h: 186.77, l: 180.05, c: 186.4, v: 100, up: true },
      { o: 10, h: 11, l: 9, c: 9.5, v: 100, up: false },
    ]);
  });

  it('pads the range and includes levels and overlays', () => {
    const range = priceRange(toPlotBars([bar('100', '110', '90', '105')]), [204, null], 0);
    expect(range).toEqual({ min: 90, max: 204 });
    expect(priceRange([], [])).toEqual({ min: 0, max: 1 });
    const flat = priceRange(toPlotBars([bar('50', '50', '50', '50')]));
    expect(flat.max).toBeGreaterThan(flat.min);
  });

  it('maps prices to rows with the maximum at the top', () => {
    const range = { min: 100, max: 200 };
    expect(yFor(200, range, 210)).toBe(0);
    expect(yFor(100, range, 210)).toBe(210);
    expect(yFor(150, range, 210)).toBe(105);
  });

  it('snaps x to the bar slot and clamps at the edges', () => {
    expect(xFor(0, 4, 100)).toBe(12.5);
    expect(indexAt(12, 4, 100)).toBe(0);
    expect(indexAt(26, 4, 100)).toBe(1);
    expect(indexAt(-5, 4, 100)).toBe(0);
    expect(indexAt(500, 4, 100)).toBe(3);
    expect(indexAt(10, 0, 100)).toBe(-1);
  });

  it('keeps candle bodies between a hairline and 9 pt', () => {
    expect(bodyWidth(250, 300)).toBe(1); // 0.84 rounds up to a hairline
    expect(bodyWidth(10, 300)).toBe(9);
    expect(bodyWidth(63, 300)).toBeCloseTo(3.333, 2);
  });

  it('computes a simple moving average once n values exist', () => {
    expect(sma([1, 2, 3, 4, 5], 3)).toEqual([null, null, 2, 3, 4]);
  });

  it('spreads overlapping level tags apart inside the pane, keeping order', () => {
    const [target, entry, stop] = spreadTags([20, 100, 104], 20, 210);
    expect(target).toBe(20);
    expect(stop! - entry!).toBeGreaterThanOrEqual(20);
    expect(entry).toBeLessThan(stop!);
    const atBottom = spreadTags([205, 208], 20, 210);
    expect(atBottom[1]).toBeLessThanOrEqual(200);
    expect(atBottom[1]! - atBottom[0]!).toBeGreaterThanOrEqual(20);
  });
});
