import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

type Schemas = components['schemas'];

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

export const SignalType = z.enum(['momentum', 'breakout', 'pullback', 'unusual_volume', 'earnings']);
export type SignalType = z.infer<typeof SignalType>;

/** D1 (SYSTEM_DESIGN.md §11.4): `unavailable` means the client hides signals rather than showing stale ones. */
export const ScannerStatus = z.object({
  status: z.enum(['live', 'degraded', 'unavailable']),
  symbolsTotal: z.number().int(),
  lastRunAt: z.string().nullable(),
  dataAsOf: z.string().nullable(),
  errorCode: z.string().nullable(),
}) satisfies z.ZodType<Omit<Schemas['ScannerStatusDto'], 'status'> & { status: string }>;
export type ScannerStatus = z.infer<typeof ScannerStatus>;

export const ScoreComponent = z.object({ component: z.string(), weight: Decimal, value: Decimal.nullable(), contribution: Decimal });

export const Signal = z.object({
  id: z.string(),
  rank: z.number().int(),
  symbol: z.string(),
  name: z.string(),
  sector: z.string().nullable(),
  type: SignalType,
  horizon: z.enum(['intraday', 'swing', 'position']),
  price: Decimal.nullable(),
  changePct: Decimal.nullable(),
  score: z.string(),
  metrics: z.object({ relVolume: Decimal.nullable(), relStrength: z.string().nullable(), rewardRisk: Decimal.nullable(), rsi14: Decimal.nullable() }),
  reason: z.string(),
  levels: z.object({ orderType: z.enum(['buy_stop', 'limit', 'market']), entry: Decimal, stop: Decimal, target: Decimal }),
  strategy: z.object({ id: z.string(), name: z.string(), version: z.number().int() }),
  evaluatedAt: z.string(),
  dataAsOf: z.string(),
  status: z.enum(['active', 'superseded', 'expired', 'invalidated']),
  /** D4 only. */
  scoreBreakdown: z.array(ScoreComponent).nullable(),
  proposalId: z.string().nullable(),
  /** The last 20 daily closes (drawing only). */
  sparkline: z.array(Decimal),
  /** Today's volume, shares. */
  volume: z.number().int().nullable(),
  /** The signal lapses here unless a newer scan confirms it. */
  expiresAt: z.string(),
});
export type Signal = z.infer<typeof Signal>;

export const SignalPage = z.object({
  items: z.array(Signal),
  nextCursor: z.string().nullable(),
  counts: z.object({ all: z.number().int(), momentum: z.number().int(), breakout: z.number().int(), pullback: z.number().int(), unusualVolume: z.number().int(), earnings: z.number().int() }),
  scanner: z.object({ lastRunAt: z.string().nullable(), dataAsOf: z.string().nullable() }),
});
export type SignalPage = z.infer<typeof SignalPage>;

export const SignalCount = z.object({ count: z.number().int() }) satisfies z.ZodType<Schemas['SignalCountResponse']>;

export interface SignalFilters {
  type?: SignalType;
  sector?: string;
  marketCap?: 'mega' | 'large' | 'mid' | 'small';
  horizon?: 'intraday' | 'swing' | 'position';
  minScore?: number;
  sort?: 'score' | 'rr' | 'relStrength' | 'change';
  symbol?: string;
}

function query(filters: SignalFilters & { limit?: number; cursor?: string }): string {
  const entries = Object.entries(filters).filter((e): e is [string, string | number] => e[1] !== undefined && e[1] !== '');
  return entries.length ? `?${new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString()}` : '';
}

/** Scanner and signals D1–D4. */
export function scannerApi(client: ApiClient) {
  return {
    status: () => client.request('/api/v1/scanner/status', { schema: ScannerStatus }),
    list: (filters: SignalFilters = {}, page: { limit?: number; cursor?: string } = {}) =>
      client.request(`/api/v1/signals${query({ ...filters, ...page })}`, { schema: SignalPage }),
    count: (filters: SignalFilters = {}) => client.request(`/api/v1/signals/count${query(filters)}`, { schema: SignalCount }),
    get: (id: string) => client.request(`/api/v1/signals/${encodeURIComponent(id)}`, { schema: Signal }),
  };
}
