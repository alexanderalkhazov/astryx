import { z } from 'zod';

import type { ApiClient } from './client';

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

export const StrategyStatus = z.enum(['active', 'active_guarded', 'paused']);
export type StrategyStatus = z.infer<typeof StrategyStatus>;

/** The four headline metrics of a run, as fractions (CAGR, max drawdown, win rate) and a ratio (Sharpe). */
export const HeadlineMetrics = z.object({ cagr: Decimal, sharpe: Decimal, maxDrawdown: Decimal, winRate: Decimal });
export type HeadlineMetrics = z.infer<typeof HeadlineMetrics>;

/** A run's full-period metrics: the headline four plus the rest of design 11's eight. */
export const FullMetrics = HeadlineMetrics.extend({
  trades: z.number().int(),
  profitFactor: Decimal,
  avgTradePct: Decimal,
  expectancyR: Decimal,
  multiple: Decimal,
});
export type FullMetrics = z.infer<typeof FullMetrics>;

export const RunMetrics = z.object({ full: FullMetrics, inSample: HeadlineMetrics, outOfSample: HeadlineMetrics });
export type RunMetrics = z.infer<typeof RunMetrics>;

/** ST1 item (SYSTEM_DESIGN.md §11.9). Live results stay illustrative until portfolio attribution exists. */
export const StrategySummary = z.object({
  id: z.string(),
  code: z.string(),
  name: z.string(),
  description: z.string(),
  status: StrategyStatus,
  displayOrder: z.number().int(),
  version: z.number().int(),
  liveSince: z.string().nullable(),
  live: z
    .object({ sinceInceptionPct: Decimal, mtdPct: Decimal, mtdPnl: Decimal, mtdTrades: z.number().int(), illustrative: z.boolean() })
    .nullable(),
  metrics: HeadlineMetrics.nullable(),
  sparkline: z.array(Decimal),
  backtestStart: z.string().nullable(),
  backtestEnd: z.string().nullable(),
  /** Drawdown guard (T-097): drawdown from the window's high and the warn/pause thresholds, as fractions. */
  guard: z.object({ drawdown: Decimal, warnAt: Decimal, pauseAt: Decimal, windowDays: z.number().int(), asOf: z.string() }).nullable(),
  /** False once a real backtest (T-160) replaced the seeded illustrative figures; absent means illustrative. */
  backtestIllustrative: z.boolean().optional(),
});
export type StrategySummary = z.infer<typeof StrategySummary>;

const ParameterValue = z.union([z.number(), z.string()]).nullable();

export const StrategyParameter = z.object({
  key: z.string(),
  label: z.string(),
  unit: z.string(),
  default: ParameterValue,
  min: ParameterValue,
  max: ParameterValue,
  step: ParameterValue,
  value: ParameterValue,
});
export type StrategyParameter = z.infer<typeof StrategyParameter>;

/** ST3. */
export const StrategyDetail = z.object({
  summary: StrategySummary,
  rules: z.array(z.string()),
  version: z.object({
    id: z.string(),
    number: z.number().int(),
    state: z.string(),
    codeVersion: z.string(),
    parameters: z.array(StrategyParameter),
  }),
  latestRun: z
    .object({
      id: z.string(),
      runNumber: z.number().int(),
      status: z.string(),
      periodStart: z.string(),
      periodEnd: z.string(),
      oosStart: z.string(),
      finishedAt: z.string().nullable(),
      metrics: RunMetrics,
    })
    .nullable(),
  todaysSignals: z.array(
    z.object({ id: z.string(), symbol: z.string(), type: z.string(), score: Decimal, entry: Decimal, evaluatedAt: z.string() }),
  ),
});
export type StrategyDetail = z.infer<typeof StrategyDetail>;

const DatedValue = z.tuple([z.string(), Decimal]);

/** ST4: monthly curves (equity in dollars, drawdown and monthly returns as fractions). */
export const BacktestRun = z.object({
  id: z.string(),
  strategyId: z.string(),
  runNumber: z.number().int(),
  version: z.number().int(),
  status: z.string(),
  periodStart: z.string(),
  periodEnd: z.string(),
  oosStart: z.string(),
  initialCapital: Decimal,
  assumptions: z
    .object({
      fees: z.string(),
      slippageBps: z.number(),
      universe: z.string(),
      survivorship: z.string(),
      benchmark: z.object({ name: z.string(), multiple: Decimal }).optional(),
    })
    .passthrough(),
  metrics: RunMetrics,
  equityCurve: z.array(DatedValue),
  drawdownCurve: z.array(DatedValue),
  monthlyReturns: z.array(DatedValue),
  finishedAt: z.string().nullable(),
  errorCode: z.string().nullable(),
});
export type BacktestRun = z.infer<typeof BacktestRun>;

/** ST2: growth of $1 on each latest run, with its full-period metrics. */
export const StrategyComparison = z.object({
  series: z.array(z.object({ strategyId: z.string(), name: z.string(), points: z.array(z.object({ d: z.string(), growth: Decimal })) })),
  metrics: z.array(z.object({ strategyId: z.string(), name: z.string(), metrics: FullMetrics.nullable() })),
});
export type StrategyComparison = z.infer<typeof StrategyComparison>;

/** Strategies ST1–ST4 (read-only; configure and re-run are Phase 6+). */
export function strategiesApi(client: ApiClient) {
  return {
    list: () => client.request('/api/v1/strategies', { schema: z.object({ items: z.array(StrategySummary) }) }),
    compare: (ids: readonly string[]) =>
      client.request(`/api/v1/strategies/compare?ids=${ids.map(encodeURIComponent).join(',')}`, { schema: StrategyComparison }),
    get: (idOrCode: string) => client.request(`/api/v1/strategies/${encodeURIComponent(idOrCode)}`, { schema: StrategyDetail }),
    run: (idOrCode: string, runId = 'latest') =>
      client.request(`/api/v1/strategies/${encodeURIComponent(idOrCode)}/runs/${encodeURIComponent(runId)}`, { schema: BacktestRun }),
  };
}
