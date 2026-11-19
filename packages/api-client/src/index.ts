// Typed client for the Astryx API (SYSTEM_DESIGN.md §8.8).
// Zod schemas validate responses at runtime and `satisfies` the types generated from apps/api/openapi.json
// (pnpm api:gen, T-010), so server DTO changes surface in `pnpm typecheck`.
import { z } from 'zod';

import type { components } from './generated/schema';

export { ApiError, isApiError, toApiError } from './errors';
export { createApiClient, type ApiClient, type ApiClientOptions, type ApiResponse, type RequestOptions } from './client';
export { authApi, CurrentUser, RegisterResponse, TokenResponse } from './auth';
export {
  Bar,
  Bars,
  Breadth,
  Fundamentals,
  IndicatorSnapshot,
  IndicatorValues,
  Instrument,
  Levels,
  MarketSession,
  marketApi,
  NewsItem,
  NewsList,
  Quote,
  type BarsRequest,
} from './market';
export { ScannerStatus, ScoreComponent, Signal, SignalCount, SignalPage, SignalType, scannerApi, type SignalFilters } from './scanner';
export { SearchItem, SearchResults, searchApi } from './search';
export { Watchlist, WatchlistItem, Watchlists, watchlistETag, watchlistsApi } from './watchlists';
export type { components as ApiSchemas } from './generated/schema';

/** Money, prices, quantities and percents travel as decimal strings — never JS numbers. */
export const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');
export type Decimal = z.infer<typeof Decimal>;

export const SystemStatus = z.object({
  database: z.string(),
  migrations: z.string(),
  ai: z.object({ status: z.string(), model: z.string().nullable() }),
  marketData: z.object({
    provider: z.string().nullable(),
    // Freshness (SYSTEM_DESIGN.md §20.5); drives the connection-lost banner.
    quotesAsOf: z.string().nullable(),
    quotesStale: z.boolean(),
    dailyBarsThrough: z.string().nullable(),
    dailyBarsStale: z.boolean(),
    /** The latest quote sync could not reach the provider (T-050): the banner shows without waiting for quotes to age. */
    feedDown: z.boolean(),
  }),
  broker: z.object({ adapter: z.string().nullable(), mode: z.literal('paper') }),
  features: z.object({ liveTrading: z.literal(false) }),
}) satisfies z.ZodType<components['schemas']['SystemStatusDto']>;
export type SystemStatus = z.infer<typeof SystemStatus>;

export async function getSystemStatus(baseUrl: string, signal?: AbortSignal): Promise<SystemStatus> {
  const response = await fetch(`${baseUrl}/api/v1/system/status`, signal ? { signal } : {});
  if (!response.ok) throw new Error(`System status failed: HTTP ${response.status}`);
  return SystemStatus.parse(await response.json());
}
export { AnalysisJob, AnalysisJobStatus, AnalysisPage, AnalysisResult, AnalysisSummary, analysesApi } from './analyses';
export { CheckStatus as ProposalCheckStatus, Proposal, RiskAssessment, RiskCheck, RISK_CHOICES, proposalsApi } from './proposals';
export { PaperOrder, paperApi } from './paper';
export {
  Allocation,
  AttentionItem,
  ClosedTrade,
  ClosePreview,
  Exposure,
  PaperReset,
  PositionDetail,
  Performance,
  type PerformanceRange,
  PortfolioPosition,
  PortfolioSummary,
  portfolioApi,
} from './portfolio';
export { ActivityItem, ActivityPage, NotificationCategory, notificationsApi } from './notifications';
export {
  BacktestRun,
  FullMetrics,
  HeadlineMetrics,
  RunMetrics,
  StrategyComparison,
  StrategyDetail,
  StrategyParameter,
  StrategyStatus,
  StrategySummary,
  strategiesApi,
} from './strategies';
export {
  DevFaults,
  NotificationPreferences,
  RiskSettings,
  type RiskSettingsUpdate,
  riskSettingsETag,
  ScannerSettings,
  settingsApi,
} from './settings';
export { BrokerConnection, brokerApi } from './broker';
