import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

type Schemas = components['schemas'];

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

/** A search result or recent (SYSTEM_DESIGN.md §11.4 InstrumentSearchItem). */
export const SearchItem = z.object({
  symbol: z.string(),
  name: z.string(),
  sector: z.string().nullable(),
  last: Decimal.nullable(),
  changePct: Decimal.nullable(),
  held: z.boolean(),
  signalType: z.string().nullable(),
}) satisfies z.ZodType<Schemas['InstrumentSearchItem']>;
export type SearchItem = z.infer<typeof SearchItem>;

export const SearchResults = z.object({ items: z.array(SearchItem) }) satisfies z.ZodType<Schemas['SearchItemsResponse']>;

/** Search M1–M3. */
export function searchApi(client: ApiClient) {
  return {
    search: (q: string, limit = 20, signal?: AbortSignal) =>
      client.request(`/api/v1/search?${new URLSearchParams({ q, limit: String(limit) }).toString()}`, { schema: SearchResults, signal }),
    recent: () => client.request('/api/v1/search/recent', { schema: SearchResults }),
    addRecent: (symbol: string) => client.request('/api/v1/search/recent', { method: 'POST', body: { symbol } }),
  };
}
