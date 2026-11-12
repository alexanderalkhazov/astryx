// Chart geometry (SYSTEM_DESIGN.md §8.6, §20.1). Prices arrive as decimal strings and become JS numbers here
// **for pixel positioning only**; every value a user reads is formatted from the original string.

export interface PriceBar {
  t: string;
  d: string;
  o: string;
  h: string;
  l: string;
  c: string;
  v: number;
}

export interface PriceLevels {
  entry: string;
  stop: string;
  target: string;
}

/** A bar as numbers, for drawing. */
export interface PlotBar {
  o: number;
  h: number;
  l: number;
  c: number;
  v: number;
  up: boolean;
}

export interface Range {
  min: number;
  max: number;
}

export const toPlotBars = (bars: readonly PriceBar[]): PlotBar[] =>
  bars.map((b) => {
    const o = Number(b.o);
    const c = Number(b.c);
    return { o, h: Number(b.h), l: Number(b.l), c, v: b.v, up: c >= o };
  });

/** Low-to-high span of the bars, the visible overlays and levels, padded so nothing touches the pane edge. */
export function priceRange(bars: readonly PlotBar[], extra: readonly (number | null)[] = [], padding = 0.06): Range {
  let min = Infinity;
  let max = -Infinity;
  for (const b of bars) {
    if (b.l < min) min = b.l;
    if (b.h > max) max = b.h;
  }
  for (const v of extra) {
    if (v === null || !Number.isFinite(v)) continue;
    if (v < min) min = v;
    if (v > max) max = v;
  }
  if (!Number.isFinite(min)) return { min: 0, max: 1 };
  const span = max - min || Math.abs(max) * 0.02 || 1; // a flat series still gets a visible band
  return { min: min - span * padding, max: max + span * padding };
}

/** Pixel row of a price inside a pane of `height` (top = max). */
export const yFor = (price: number, range: Range, height: number) => ((range.max - price) / (range.max - range.min)) * height;

/** Centre of bar `index` when `count` bars share `width`. */
export const xFor = (index: number, count: number, width: number) => (width / count) * (index + 0.5);

/** Bar under pixel column `x`, clamped to the series. Runs on the UI thread during a scrub. */
export function indexAt(x: number, count: number, width: number): number {
  'worklet';
  if (count <= 0) return -1;
  const index = Math.floor((x / width) * count);
  return index < 0 ? 0 : index >= count ? count - 1 : index;
}

/** Candle body width: 70 % of the slot, at least a hairline, at most 9 pt (design 04). */
export const bodyWidth = (count: number, width: number) => Math.max(1, Math.min(9, (width / count) * 0.7));

/** Simple moving average for the chart overlay; null until `n` values exist. */
export function sma(values: readonly number[], n: number): (number | null)[] {
  const out: (number | null)[] = [];
  let sum = 0;
  for (let i = 0; i < values.length; i++) {
    sum += values[i]!;
    if (i >= n) sum -= values[i - n]!;
    out.push(i >= n - 1 ? sum / n : null);
  }
  return out;
}

/** Evenly spaced horizontal grid rows (design 04 draws four inside the price pane). */
export const gridRows = (height: number, rows = 4) => Array.from({ length: rows }, (_, i) => (height / (rows + 1)) * (i + 1));

/**
 * Level tags in the gutter must not overlap: tags closer than `minGap` are pushed apart, keeping their order and
 * staying inside the pane. Returns the tag centres in the input order.
 */
export function spreadTags(ys: readonly number[], minGap: number, height: number): number[] {
  const order = ys.map((y, i) => ({ y, i })).sort((a, b) => a.y - b.y);
  const placed: number[] = [];
  for (const [k, tag] of order.entries()) {
    const floor = k === 0 ? minGap / 2 : placed[k - 1]! + minGap;
    placed.push(Math.max(tag.y, floor));
  }
  // Push back up from the bottom if the stack ran out of the pane.
  for (let k = placed.length - 1; k >= 0; k--) {
    const ceiling = k === placed.length - 1 ? height - minGap / 2 : placed[k + 1]! - minGap;
    placed[k] = Math.min(placed[k]!, ceiling);
  }
  const out = new Array<number>(ys.length);
  order.forEach((tag, k) => (out[tag.i] = placed[k]!));
  return out;
}
