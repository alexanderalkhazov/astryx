import { isApiError } from '@astryx/api-client';
import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query';

import { scanner } from '@/lib/api';

import { type DiscoverFilters, toQuery } from './filters';

/** Signals refresh every minute (§8.7: signals 60 s); an unavailable scanner is retried on the same beat (design 23). */
export const SIGNALS_REFRESH_MS = 60_000;
export const PAGE_SIZE = 20;

export const SCANNER_STATUS_KEY = ['scanner', 'status'] as const;

export function useScannerStatus() {
  return useQuery({ queryKey: SCANNER_STATUS_KEY, queryFn: async () => (await scanner.status()).data, refetchInterval: SIGNALS_REFRESH_MS });
}

/** D2 pages for the current filters (`['signals', filters]`, §8.7). A 503 means the scanner is unavailable: hide, don't show stale. */
export function useSignals(filters: DiscoverFilters) {
  return useInfiniteQuery({
    queryKey: ['signals', toQuery(filters)],
    queryFn: async ({ pageParam }) => (await scanner.list(toQuery(filters), { limit: PAGE_SIZE, cursor: pageParam })).data,
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    refetchInterval: SIGNALS_REFRESH_MS,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** The live "Show N setups" count in the Filters sheet (D3). */
export function useSignalCount(filters: DiscoverFilters, enabled: boolean) {
  return useQuery({
    queryKey: ['signals', 'count', toQuery(filters)],
    queryFn: async () => (await scanner.count(toQuery(filters))).data.count,
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** Total active signals for the Discover tab badge; none while the scanner is unavailable. */
export function useSignalBadge() {
  const count = useQuery({ queryKey: ['signals', 'count', 'all'], queryFn: async () => (await scanner.count({})).data.count, refetchInterval: SIGNALS_REFRESH_MS, retry: false });
  return count.isSuccess && count.data > 0 ? count.data : undefined;
}

export const isScannerUnavailable = (error: unknown) => isApiError(error) && error.status === 503;
