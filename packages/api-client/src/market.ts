import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

type Schemas = components['schemas'];

// Runtime validation for the market endpoints (SYSTEM_DESIGN.md §11.4). Each schema `satisfies` the type generated from
// the API's OpenAPI document (pnpm api:gen), so a DTO change that is not mirrored here fails `pnpm typecheck`.

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');
const Instant = z.string();
const IsoDate = z.string().regex(/^\d{4}-\d{2}-\d{2}$/);

export const Instrument = z.object({
  symbol: z.string(),
  exchange: z.string(),
  name: z.string(),
  assetType: z.string(),
  currency: z.string(),
  sector: z.string().nullable(),
  industry: z.string().nullable(),
  marketCap: Decimal.nullable(),
  isActive: z.boolean(),
}) satisfies z.ZodType<Schemas['InstrumentDto']>;
export type Instrument = z.infer<typeof Instrument>;

export const Quote = z.object({
  symbol: z.string(),
  last: Decimal,
  change: Decimal,
  changePct: Decimal,
  open: Decimal,
  high: Decimal,
  low: Decimal,
  prevClose: Decimal,
  bid: Decimal.nullable(),
  ask: Decimal.nullable(),
  volume: z.number().int(),
  asOf: Instant,
  quality: z.string(),
  stale: z.boolean(),
  source: z.string(),
}) satisfies z.ZodType<Schemas['QuoteDto']>;
export type Quote = z.infer<typeof Quote>;

/** One bar: t = start instant, d = session date, final present (false) only on a forming bar. */
export const Bar = z.object({
  t: Instant,
  d: IsoDate,
  o: Decimal,
  h: Decimal,
  l: Decimal,
  c: Decimal,
  v: z.number().int(),
  final: z.boolean().nullable().optional(),
}) satisfies z.ZodType<Omit<Schemas['BarDto'], 'final'> & { final?: boolean | null }>;
export type Bar = z.infer<typeof Bar>;

export const Levels = z.object({ entry: Decimal, stop: Decimal, target: Decimal }) satisfies z.ZodType<Schemas['LevelsDto']>;

export const Bars = z.object({
  symbol: z.string(),
  interval: z.string(),
  adjusted: z.boolean(),
  asOf: Instant.nullable(),
  stale: z.boolean(),
  bars: z.array(Bar),
  levels: Levels.nullable(),
}) satisfies z.ZodType<Omit<Schemas['BarsDto'], 'bars'> & { bars: Bar[] }>;
export type Bars = z.infer<typeof Bars>;

export const Fundamentals = z.object({
  symbol: z.string(),
  asOf: Instant.nullable(),
  beta: Decimal.nullable(),
  peRatio: Decimal.nullable(),
  epsTtm: Decimal.nullable(),
  nextEarningsDate: IsoDate.nullable(),
  marketCap: Decimal.nullable(),
  sector: z.string().nullable(),
  industry: z.string().nullable(),
}) satisfies z.ZodType<Schemas['FundamentalsDto']>;
export type Fundamentals = z.infer<typeof Fundamentals>;

/** M7 values: presentation strings (4 dp; RSI 1 dp; relative volume 2 dp; RS rank 1–99), null when history is too short. */
export const IndicatorValues = z.object({
  sma20: Decimal.nullable(),
  sma50: Decimal.nullable(),
  ema20: Decimal.nullable(),
  rsi14: Decimal.nullable(),
  macd: Decimal.nullable(),
  macdSignal: Decimal.nullable(),
  macdHist: Decimal.nullable(),
  atr14: Decimal.nullable(),
  relVolume20: Decimal.nullable(),
  momentum20Pct: Decimal.nullable(),
  changePct1d: Decimal.nullable(),
  volatility20Ann: Decimal.nullable(),
  relStrength: z.string().nullable(),
  trend: z.enum(['uptrend', 'downtrend', 'sideways']).nullable(),
});
export type IndicatorValues = z.infer<typeof IndicatorValues>;

/** M7 IndicatorSnapshotDto. The generated type loses value nullability, so `values` is checked here only. */
export const IndicatorSnapshot = z.object({
  symbol: z.string(),
  interval: z.string(),
  asOfBar: IsoDate,
  barCount: z.number().int(),
  dataQuality: z.enum(['ok', 'gaps', 'stale', 'insufficient']),
  values: IndicatorValues,
  insufficient: z.array(z.string()),
}) satisfies z.ZodType<Omit<Schemas['IndicatorSnapshot'], 'values' | 'dataQuality'> & { values: IndicatorValues; dataQuality: string }>;
export type IndicatorSnapshot = z.infer<typeof IndicatorSnapshot>;

export const NewsItem = z.object({
  id: z.string(),
  headline: z.string(),
  source: z.string(),
  url: z.string(),
  publishedAt: Instant,
  summary: z.string().nullable(),
}) satisfies z.ZodType<Schemas['NewsItemDto']>;
export type NewsItem = z.infer<typeof NewsItem>;

export const NewsList = z.object({ items: z.array(NewsItem) }) satisfies z.ZodType<Schemas['ItemsResponseOfNewsItemDto']>;

export const Breadth = z.object({
  advancers: z.number().int(),
  decliners: z.number().int(),
  pctAbove50dma: Decimal,
  sentiment: z.enum(['risk_on', 'neutral', 'risk_off']),
  asOf: Instant,
  stale: z.boolean(),
}) satisfies z.ZodType<Schemas['BreadthDto']>;
export type Breadth = z.infer<typeof Breadth>;

export const MarketSession = z.object({
  exchange: z.string(),
  state: z.enum(['open', 'closed', 'pre', 'post']),
  sessionDate: IsoDate,
  opensAt: Instant,
  closesAt: Instant,
  isHalfDay: z.boolean(),
}) satisfies z.ZodType<Schemas['MarketSessionResponse']>;
export type MarketSession = z.infer<typeof MarketSession>;

/** A display range for charts, or an explicit interval with dates. */
export type BarsRequest =
  | { timeframe: '1D' | '5D' | '1M' | '3M' | '6M' | '1Y' | 'ALL'; limit?: number }
  | { interval: '1m' | '5m' | '15m' | '1h' | '1d'; from?: string; to?: string; limit?: number };

function query(params: Record<string, string | number | undefined>): string {
  const entries = Object.entries(params).filter((e): e is [string, string | number] => e[1] !== undefined);
  return entries.length ? `?${new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString()}` : '';
}

/** Market endpoints M4–M11 (M7 is served by the Quant module). */
export function marketApi(client: ApiClient) {
  const symbolPath = (symbol: string) => encodeURIComponent(symbol.toUpperCase());
  return {
    instrument: (symbol: string) => client.request(`/api/v1/instruments/${symbolPath(symbol)}`, { schema: Instrument }),
    quote: (symbol: string) => client.request(`/api/v1/market/${symbolPath(symbol)}/quote`, { schema: Quote }),
    bars: (symbol: string, request: BarsRequest) =>
      client.request(`/api/v1/market/${symbolPath(symbol)}/bars${query(request)}`, { schema: Bars }),
    indicators: (symbol: string) =>
      client.request(`/api/v1/market/${symbolPath(symbol)}/indicators?interval=1d`, { schema: IndicatorSnapshot }),
    fundamentals: (symbol: string) => client.request(`/api/v1/instruments/${symbolPath(symbol)}/fundamentals`, { schema: Fundamentals }),
    news: (symbol: string, limit = 10) =>
      client.request(`/api/v1/instruments/${symbolPath(symbol)}/news${query({ limit })}`, { schema: NewsList }),
    breadth: () => client.request('/api/v1/market/breadth', { schema: Breadth }),
    session: () => client.request('/api/v1/market/session', { schema: MarketSession }),
  };
}
