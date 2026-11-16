import { emaLine, macdLines, rsiLine } from '../paneMath';

describe('pane math (display only, §13.3 formulas)', () => {
  it('seeds the EMA with the SMA', () => {
    expect(emaLine([1, 2, 3, 4, 5], 3)).toEqual([null, null, 2, 3, 4]);
  });

  it('gives RSI 100 on all gains, 50 when flat, and nothing before n + 1 bars', () => {
    const rising = Array.from({ length: 20 }, (_, i) => i + 1);
    expect(
      rsiLine(rising)
        .slice(14)
        .every((v) => v === 100),
    ).toBe(true);
    expect(
      rsiLine(new Array(20).fill(5))
        .slice(14)
        .every((v) => v === 50),
    ).toBe(true);
    expect(rsiLine(rising.slice(0, 14)).every((v) => v === null)).toBe(true);
  });

  it('applies Wilder smoothing (hand-computed, n = 2)', () => {
    // Δ = +1, −1, +1, +1 → AG/AL 0.5/0.5 (50), then 0.75/0.25 (RS 3 → 75), then 0.875/0.125 (RS 7 → 87.5).
    expect(rsiLine([1, 2, 1, 2, 3], 2)).toEqual([null, null, 50, 75, 87.5]);
  });

  it('starts MACD signal and histogram at bar 34', () => {
    const closes = Array.from({ length: 40 }, (_, i) => 100 + i + Math.sin(i));
    const { macd, signal, hist } = macdLines(closes);
    expect(macd.findIndex((v) => v !== null)).toBe(25);
    expect(signal.findIndex((v) => v !== null)).toBe(33);
    expect(hist[39]).toBeCloseTo(macd[39]! - signal[39]!, 10);
  });
});
