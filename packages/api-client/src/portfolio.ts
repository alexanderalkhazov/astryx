import { z } from 'zod';

import type { ApiClient } from './client';

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

/** P1 (SYSTEM_DESIGN.md §11.8). */
export const PortfolioSummary = z.object({
  id: z.string(),
  mode: z.literal('paper'),
  currency: z.string(),
  equity: Decimal,
  cash: Decimal,
  cashAvailable: Decimal,
  invested: Decimal,
  unrealizedPnl: Decimal,
  dayPnl: Decimal,
  dayPnlPct: Decimal.nullable(),
  allTimePnl: Decimal,
  allTimeReturnPct: Decimal,
  realizedPnl: Decimal,
  openPositions: z.number().int(),
  halted: z.boolean(),
  asOf: z.string(),
  stale: z.boolean(),
  /** Why entries are paused: the daily loss halt (until next session) or a reconciliation mismatch (until reviewed). */
  haltReason: z.enum(['daily_loss', 'reconciliation']).nullable().optional(),
});
export type PortfolioSummary = z.infer<typeof PortfolioSummary>;

/** P2 row: open rows carry valuation and weight against the cap; closed rows the exit. */
export const PortfolioPosition = z.object({
  id: z.string(),
  symbol: z.string(),
  name: z.string(),
  status: z.enum(['open', 'closed']),
  quantity: Decimal,
  avgEntryPrice: Decimal,
  last: Decimal.nullable(),
  marketValue: Decimal.nullable(),
  unrealizedPnl: Decimal.nullable(),
  unrealizedPct: Decimal.nullable(),
  weightPct: Decimal.nullable(),
  weightCapPct: Decimal,
  overCap: z.boolean(),
  stopPrice: Decimal.nullable(),
  targetPrice: Decimal.nullable(),
  stopDistancePct: Decimal.nullable(),
  nearStop: z.boolean(),
  isNew: z.boolean(),
  openedAt: z.string(),
  exitPrice: Decimal.nullable(),
  exitReason: z.string().nullable(),
  realizedPnl: Decimal.nullable(),
  closedAt: z.string().nullable(),
});
export type PortfolioPosition = z.infer<typeof PortfolioPosition>;

export const PositionPage = z.object({ items: z.array(PortfolioPosition), nextCursor: z.string().nullable() });

export const Allocation = z.object({
  sectors: z.array(z.object({ sector: z.string(), value: Decimal, pct: Decimal, warning: z.boolean(), overLimit: z.boolean() })),
  assets: z.array(z.object({ asset: z.string(), value: Decimal, pct: Decimal })),
  sectorWarningPct: Decimal,
  maxSectorExposurePct: Decimal,
});
export type Allocation = z.infer<typeof Allocation>;

/** P8. `betaWeightedPct` is null when a beta is missing. */
export const Exposure = z.object({
  grossPct: Decimal,
  betaWeightedPct: Decimal.nullable(),
  openRiskToStops: Decimal,
  concentrationWarnings: z.array(z.object({ kind: z.string(), subject: z.string(), pct: Decimal, limit: Decimal, message: z.string() })),
});
export type Exposure = z.infer<typeof Exposure>;

export const Performance = z.object({
  range: z.string(),
  benchmark: z.string(),
  series: z.array(z.object({ t: z.string(), equity: Decimal, benchmark: Decimal.nullable() })),
  metrics: z.object({
    twrPct: Decimal.nullable(),
    benchmarkPct: Decimal.nullable(),
    maxDrawdownPct: Decimal,
    winRate: Decimal.nullable(),
    avgWin: Decimal.nullable(),
    avgLoss: Decimal.nullable(),
    trades: z.number().int(),
  }),
});
export type Performance = z.infer<typeof Performance>;

export const ClosedTrade = z.object({
  positionId: z.string(),
  symbol: z.string(),
  quantity: Decimal,
  entryPrice: Decimal,
  exitPrice: Decimal,
  exitReason: z.string(),
  realizedPnl: Decimal,
  returnPct: Decimal,
  openedAt: z.string(),
  closedAt: z.string(),
  holdingDays: z.number().int(),
});
export type ClosedTrade = z.infer<typeof ClosedTrade>;

export const ClosedTradePage = z.object({ items: z.array(ClosedTrade), nextCursor: z.string().nullable() });

export const AttentionItem = z.object({
  kind: z.string(),
  title: z.string(),
  body: z.string(),
  deepLink: z.string(),
  severity: z.enum(['info', 'warning', 'error']),
});
export type AttentionItem = z.infer<typeof AttentionItem>;

/** P3 (§11.8): facts, the stop → target track and lineage. */
export const PositionDetail = z.object({
  id: z.string(),
  symbol: z.string(),
  name: z.string(),
  status: z.enum(['open', 'closed']),
  isNew: z.boolean(),
  quantity: Decimal,
  avgEntryPrice: Decimal,
  costBasis: Decimal,
  last: Decimal.nullable(),
  marketValue: Decimal.nullable(),
  unrealizedPnl: Decimal.nullable(),
  unrealizedPct: Decimal.nullable(),
  track: z.object({
    stop: Decimal.nullable(),
    entry: Decimal,
    current: Decimal.nullable(),
    target: Decimal.nullable(),
    progressPct: Decimal.nullable(),
  }),
  feesPaid: Decimal,
  openedAt: z.string(),
  entryOrderId: z.string().nullable(),
  generatedBy: z
    .object({ source: z.string(), strategyId: z.string().nullable(), strategyName: z.string().nullable(), at: z.string().nullable() })
    .nullable(),
  exitPrice: Decimal.nullable(),
  exitReason: z.string().nullable(),
  realizedPnl: Decimal.nullable(),
  closedAt: z.string().nullable(),
  quoteAsOf: z.string().nullable(),
});
export type PositionDetail = z.infer<typeof PositionDetail>;

/** T10: what closing now would do (server-computed). */
export const ClosePreview = z.object({
  quantity: Decimal,
  marketValue: Decimal,
  estExitPrice: Decimal,
  estFees: Decimal,
  estRealizedPnl: Decimal,
  asOf: z.string(),
});
export type ClosePreview = z.infer<typeof ClosePreview>;

export type PerformanceRange = '1D' | '1W' | '1M' | '3M' | '1Y' | 'ALL';

/** P11 response: what a paper reset did. */
export const PaperReset = z.object({
  portfolioId: z.string(),
  cashBalance: Decimal,
  startingCash: Decimal,
  closedPositions: z.number().int(),
  cancelledOrders: z.number().int(),
  resetCount: z.number().int(),
  epochStartedAt: z.string(),
});
export type PaperReset = z.infer<typeof PaperReset>;

/** Portfolio P1, P2, P5–P9 and P11. */
export function portfolioApi(client: ApiClient) {
  return {
    summary: () => client.request('/api/v1/portfolio', { schema: PortfolioSummary }),
    sync: () => client.request('/api/v1/portfolio/sync', { method: 'POST', schema: PortfolioSummary }),
    strategyMtd: () =>
      client.request('/api/v1/strategies/summary?period=mtd', {
        schema: z.object({
          period: z.string(),
          items: z.array(
            z.object({
              strategyId: z.string(),
              name: z.string(),
              pnl: Decimal,
              pnlPct: Decimal,
              trades: z.number().int(),
              illustrative: z.boolean(),
            }),
          ),
        }),
      }),
    positions: (status: 'open' | 'closed' = 'open') =>
      client.request(`/api/v1/portfolio/positions?status=${status}`, { schema: PositionPage }),
    allocation: () => client.request('/api/v1/portfolio/allocation', { schema: Allocation }),
    exposure: () => client.request('/api/v1/portfolio/exposure', { schema: Exposure }),
    performance: (range: PerformanceRange) => client.request(`/api/v1/portfolio/performance?range=${range}`, { schema: Performance }),
    trades: () => client.request('/api/v1/portfolio/trades', { schema: ClosedTradePage }),
    positionBySymbol: (symbol: string) =>
      client.request(`/api/v1/positions/by-symbol/${encodeURIComponent(symbol)}`, { schema: PositionDetail }),
    closePreview: (positionId: string) =>
      client.request(`/api/v1/positions/${encodeURIComponent(positionId)}/close-preview`, { method: 'POST', schema: ClosePreview }),
    close: (positionId: string, idempotencyKey: string) =>
      client.request(`/api/v1/positions/${encodeURIComponent(positionId)}/close`, {
        method: 'POST',
        body: {},
        idempotencyKey,
        schema: z.object({ position: z.object({ status: z.string(), realizedPnl: Decimal.nullable() }) }),
      }),
    /** P11: closes everything at the last price and restores the starting cash. `confirm` must be RESET. */
    reset: (idempotencyKey: string) =>
      client.request('/api/v1/portfolio/reset', { method: 'POST', body: { confirm: 'RESET' }, idempotencyKey, schema: PaperReset }),
    attention: () => client.request('/api/v1/portfolio/attention', { schema: z.object({ items: z.array(AttentionItem) }) }),
  };
}
