import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

type Schemas = components['schemas'];

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

/** A watched instrument with its latest price (SYSTEM_DESIGN.md §11.5 W1). */
export const WatchlistItem = z.object({
  symbol: z.string(),
  name: z.string(),
  sector: z.string().nullable(),
  last: Decimal.nullable(),
  changePct: Decimal.nullable(),
  hasActiveSignal: z.boolean(),
  held: z.boolean(),
  note: z.string().nullable(),
  addedAt: z.string(),
}) satisfies z.ZodType<Schemas['WatchlistItemDto']>;
export type WatchlistItem = z.infer<typeof WatchlistItem>;

export const Watchlist = z.object({
  id: z.string(),
  name: z.string(),
  isDefault: z.boolean(),
  sortOrder: z.number().int(),
  /** Concurrency version: send `"v{version}"` as If-Match to rename or reorder. */
  version: z.number().int(),
  items: z.array(WatchlistItem),
}) satisfies z.ZodType<Schemas['WatchlistDto']>;
export type Watchlist = z.infer<typeof Watchlist>;

export const Watchlists = z.object({ items: z.array(Watchlist) }) satisfies z.ZodType<Schemas['WatchlistsResponse']>;

/** The ETag for a list version. */
export const watchlistETag = (list: Pick<Watchlist, 'version'>) => `"v${list.version}"`;

const path = (id: string) => `/api/v1/watchlists/${encodeURIComponent(id)}`;

/** Watchlists W1–W8. `id` is a list id or `default`. */
export function watchlistsApi(client: ApiClient) {
  return {
    list: () => client.request('/api/v1/watchlists', { schema: Watchlists }),
    create: (name: string) => client.request('/api/v1/watchlists', { method: 'POST', body: { name }, schema: Watchlist }),
    update: (list: Pick<Watchlist, 'id' | 'version'>, changes: { name?: string; sortOrder?: number }) =>
      client.request(path(list.id), { method: 'PATCH', body: changes, ifMatch: watchlistETag(list), schema: Watchlist }),
    remove: (id: string) => client.request(path(id), { method: 'DELETE' }),
    addItem: (id: string, symbol: string, note?: string) =>
      client.request(`${path(id)}/items`, { method: 'POST', body: { symbol, note }, schema: WatchlistItem }),
    removeItem: (id: string, symbol: string) => client.request(`${path(id)}/items/${encodeURIComponent(symbol)}`, { method: 'DELETE' }),
    /** Star on: idempotent add to the default list (W7). */
    star: (symbol: string) => client.request(`/api/v1/watchlists/default/items/${encodeURIComponent(symbol)}`, { method: 'PUT' }),
    /** Star off: idempotent remove from the default list (W6). */
    unstar: (symbol: string) => client.request(`/api/v1/watchlists/default/items/${encodeURIComponent(symbol)}`, { method: 'DELETE' }),
    /** Puts the items in this order; `symbols` must be exactly the list's items (W8). */
    reorder: (list: Pick<Watchlist, 'id' | 'version'>, symbols: string[]) =>
      client.request(`${path(list.id)}/order`, { method: 'PUT', body: { symbols }, ifMatch: watchlistETag(list), schema: Watchlist }),
  };
}
