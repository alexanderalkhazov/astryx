// Typed client for the Astryx API (SYSTEM_DESIGN.md §8.8).
// Zod schemas validate responses at runtime and `satisfies` the types generated from apps/api/openapi.json
// (pnpm api:gen, T-010), so server DTO changes surface in `pnpm typecheck`.
import { z } from 'zod';

export { ApiError, isApiError, toApiError } from './errors';
export { createApiClient, type ApiClient, type ApiClientOptions, type ApiResponse, type RequestOptions } from './client';
export { authApi, CurrentUser, RegisterResponse, TokenResponse } from './auth';
export {
  Bar,
  Bars,
  Breadth,
  Fundamentals,
  Instrument,
  Levels,
  MarketSession,
  marketApi,
  NewsItem,
  NewsList,
  Quote,
  type BarsRequest,
} from './market';
export { SearchItem, SearchResults, searchApi } from './search';
export { Watchlist, WatchlistItem, Watchlists, watchlistETag, watchlistsApi } from './watchlists';
export type { components as ApiSchemas } from './generated/schema';

/** Money, prices, quantities and percents travel as decimal strings — never JS numbers. */
export const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');
export type Decimal = z.infer<typeof Decimal>;

export const SystemStatus = z.object({
  database: z.string(),
  migrations: z.string().optional(),
  ai: z.object({ status: z.string(), model: z.string().nullable() }),
  marketData: z.object({
    provider: z.string().nullable(),
    // Freshness (SYSTEM_DESIGN.md §20.5); drives the connection-lost banner.
    quotesAsOf: z.string().nullable().optional(),
    quotesStale: z.boolean().optional(),
    dailyBarsThrough: z.string().nullable().optional(),
    dailyBarsStale: z.boolean().optional(),
    /** The latest quote sync could not reach the provider (T-050): the banner shows without waiting for quotes to age. */
    feedDown: z.boolean().optional(),
  }),
  broker: z.object({ adapter: z.string().nullable(), mode: z.literal('paper') }),
  features: z.object({ liveTrading: z.literal(false) }),
});
export type SystemStatus = z.infer<typeof SystemStatus>;

export async function getSystemStatus(baseUrl: string, signal?: AbortSignal): Promise<SystemStatus> {
  const response = await fetch(`${baseUrl}/api/v1/system/status`, signal ? { signal } : {});
  if (!response.ok) throw new Error(`System status failed: HTTP ${response.status}`);
  return SystemStatus.parse(await response.json());
}
