import { useQuery } from '@tanstack/react-query';

import { isScannerUnavailable, SIGNALS_REFRESH_MS } from '@/features/discover/useSignals';
import { scanner } from '@/lib/api';

/**
 * The symbol's highest-scoring active signal with its score breakdown (D2 filtered by symbol, then D4), or null when
 * the scan found none. `unavailable` mirrors Discover: when the scanner is down, signals are hidden, not shown stale.
 */
export function useActiveSignal(symbol: string, enabled: boolean) {
  const query = useQuery({
    queryKey: ['signals', 'symbol', symbol],
    queryFn: async () => {
      const page = (await scanner.list({ symbol, sort: 'score' }, { limit: 1 })).data;
      const top = page.items[0];
      return top ? (await scanner.get(top.id)).data : null;
    },
    enabled,
    refetchInterval: SIGNALS_REFRESH_MS,
    retry: false,
  });
  return { ...query, unavailable: isScannerUnavailable(query.error) };
}
