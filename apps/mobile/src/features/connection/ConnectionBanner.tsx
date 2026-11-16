import { StatusBanner } from '@/components/StatusBanner';
import { formatEtTime } from '@/lib/format';

import { type ConnectionProblem, useConnection } from './useConnection';

const TITLES: Record<ConnectionProblem, string> = {
  offline: 'No internet connection',
  unreachable: "Can't reach Astryx",
  feed_down: 'Market data connection lost',
  stale: 'Market data is delayed',
};

/**
 * Connection-lost banner (design 22, SYSTEM_DESIGN.md §8.11): never let stale numbers pass for live ones. Renders
 * nothing while everything is live. `pollMs` exists for tests; the app uses the 15 s default.
 */
export function ConnectionBanner({ pollMs }: { pollMs?: number }) {
  const { problem, lastSyncAt, retry } = useConnection(pollMs);
  if (!problem) return null;
  const since = lastSyncAt
    ? `Values below are from the last sync at ${formatEtTime(lastSyncAt)} and may be stale.`
    : 'Values below may be stale.';
  return (
    <StatusBanner
      tone="error"
      title={TITLES[problem]}
      body={`${since} Approvals are disabled until the connection returns.`}
      action={{ label: 'Retry', onPress: retry }}
      testID="connection-banner"
    />
  );
}
