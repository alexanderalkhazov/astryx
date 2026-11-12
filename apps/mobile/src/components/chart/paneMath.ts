// Indicator lines for the RSI and MACD panes (SYSTEM_DESIGN.md §8.4.3). These are numbers **for pixel positioning
// only**: the values a user reads come from the server's M7 snapshot. Formulas follow §13.3 (Wilder RSI, SMA-seeded EMA).

export function emaLine(values: readonly number[], n: number): (number | null)[] {
  const out: (number | null)[] = new Array(values.length).fill(null);
  if (values.length < n) return out;
  const k = 2 / (n + 1);
  let ema = values.slice(0, n).reduce((a, b) => a + b, 0) / n;
  out[n - 1] = ema;
  for (let i = n; i < values.length; i++) {
    ema = values[i]! * k + ema * (1 - k);
    out[i] = ema;
  }
  return out;
}

export function rsiLine(closes: readonly number[], n = 14): (number | null)[] {
  const out: (number | null)[] = new Array(closes.length).fill(null);
  if (closes.length < n + 1) return out;
  let gain = 0;
  let loss = 0;
  for (let i = 1; i <= n; i++) {
    const d = closes[i]! - closes[i - 1]!;
    gain += Math.max(d, 0);
    loss += Math.max(-d, 0);
  }
  gain /= n;
  loss /= n;
  const value = () => (loss === 0 ? (gain === 0 ? 50 : 100) : 100 - 100 / (1 + gain / loss));
  out[n] = value();
  for (let i = n + 1; i < closes.length; i++) {
    const d = closes[i]! - closes[i - 1]!;
    gain = (gain * (n - 1) + Math.max(d, 0)) / n;
    loss = (loss * (n - 1) + Math.max(-d, 0)) / n;
    out[i] = value();
  }
  return out;
}

export interface MacdLines {
  macd: (number | null)[];
  signal: (number | null)[];
  hist: (number | null)[];
}

export function macdLines(closes: readonly number[], fast = 12, slow = 26, signal = 9): MacdLines {
  const empty = () => new Array<number | null>(closes.length).fill(null);
  if (closes.length < slow + signal - 1) return { macd: empty(), signal: empty(), hist: empty() };
  const f = emaLine(closes, fast);
  const s = emaLine(closes, slow);
  const macd = empty();
  for (let i = slow - 1; i < closes.length; i++) macd[i] = f[i]! - s[i]!;
  const onLine = emaLine(macd.slice(slow - 1) as number[], signal);
  const sig = empty();
  const hist = empty();
  onLine.forEach((v, k) => {
    if (v === null) return;
    const i = slow - 1 + k;
    sig[i] = v;
    hist[i] = macd[i]! - v;
  });
  return { macd, signal: sig, hist };
}
