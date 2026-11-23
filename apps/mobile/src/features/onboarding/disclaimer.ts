import { useEffect } from 'react';
import { create } from 'zustand';

import { secureStorage } from '@/lib/secureStorage';

/** The three statements every user accepts once before using the app (T-118, SYSTEM_DESIGN.md §1, §12.6). */
export const DISCLAIMER_POINTS = [
  { title: 'Paper trading only', body: 'Every order goes to a simulated account or your Alpaca paper account. No real money moves.' },
  {
    title: 'Not investment advice',
    body: 'Signals, proposals and analyses are research to help you think. You decide, and nothing is a recommendation.',
  },
  {
    title: 'AI research is experimental',
    body: 'AI summaries can be wrong or out of date. The risk engine, not the AI, decides whether a trade can be approved.',
  },
] as const;

/** Per account on this device: true once accepted, false when not, absent until read from the Keychain. */
const useDisclaimerStore = create<{ accepted: Record<string, boolean> }>(() => ({ accepted: {} }));

/** Whether this account accepted the disclaimer on this device; undefined while unknown (no user yet, or loading). */
export function useDisclaimerAccepted(userId: string | undefined): boolean | undefined {
  const accepted = useDisclaimerStore((s) => (userId ? s.accepted[userId] : undefined));
  useEffect(() => {
    if (!userId || accepted !== undefined) return;
    void Promise.resolve(secureStorage.hasAcceptedDisclaimer(userId)).then((value) =>
      useDisclaimerStore.setState((s) => ({ accepted: { ...s.accepted, [userId]: value } })),
    );
  }, [userId, accepted]);
  return accepted;
}

export async function acceptDisclaimer(userId: string): Promise<void> {
  await secureStorage.acceptDisclaimer(userId);
  useDisclaimerStore.setState((s) => ({ accepted: { ...s.accepted, [userId]: true } }));
}
