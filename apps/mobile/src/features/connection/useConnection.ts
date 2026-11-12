import { getSystemStatus } from '@astryx/api-client';
import { useNetInfo } from '@react-native-community/netinfo';
import { useQuery, useQueryClient } from '@tanstack/react-query';

import { apiUrl } from '@/lib/config';

export const SYSTEM_STATUS_KEY = ['system-status'] as const;

/** Polled so a dead feed shows within one quote-sync cycle plus one poll (< 90 s, T-050). */
export const STATUS_POLL_MS = 15_000;

/** Why the numbers on screen may not be live, most fundamental first. */
export type ConnectionProblem = 'offline' | 'unreachable' | 'feed_down' | 'stale';

export interface ConnectionState {
  problem: ConnectionProblem | null;
  /** When the newest stored quote was true; the banner says values are from then. */
  lastSyncAt: string | null;
  /** Approvals need live data (§20.5): any problem blocks them. */
  approvalsBlocked: boolean;
  retry: () => void;
}

/** Network (NetInfo) plus `/system/status` polling: the source of the connection-lost banner (SYSTEM_DESIGN.md §8.11). */
export function useConnection(pollMs = STATUS_POLL_MS): ConnectionState {
  const queryClient = useQueryClient();
  const net = useNetInfo();
  const status = useQuery({
    queryKey: SYSTEM_STATUS_KEY,
    queryFn: ({ signal }) => getSystemStatus(apiUrl, signal),
    refetchInterval: pollMs,
    retry: false,
    // Keep probing while "offline", so reconnecting is noticed even if NetInfo is late.
    networkMode: 'always',
  });

  const market = status.data?.marketData;
  const problem: ConnectionProblem | null =
    net.isConnected === false ? 'offline' : status.isError ? 'unreachable' : market?.feedDown ? 'feed_down' : market?.quotesStale ? 'stale' : null;

  return {
    problem,
    lastSyncAt: market?.quotesAsOf ?? null,
    approvalsBlocked: problem !== null,
    retry: () => {
      void status.refetch();
      void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] !== SYSTEM_STATUS_KEY[0] });
    },
  };
}
