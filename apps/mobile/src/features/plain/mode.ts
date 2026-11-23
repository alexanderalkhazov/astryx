import { useEffect } from 'react';
import { create } from 'zustand';

import { secureStorage } from '@/lib/secureStorage';

/** Simple shows plain-language explanations and tap-to-explain terms; Pro shows the trader's view only (T-161). */
export type ViewMode = 'simple' | 'pro';

const useModeStore = create<{ modes: Record<string, ViewMode> }>(() => ({ modes: {} }));

/** The account's mode on this device; Simple until chosen otherwise (and while loading). */
export function useViewMode(userId: string | undefined): ViewMode {
  const mode = useModeStore((s) => (userId ? s.modes[userId] : undefined));
  useEffect(() => {
    if (!userId || mode !== undefined) return;
    void secureStorage
      .getViewMode(userId)
      .then((stored) => useModeStore.setState((s) => ({ modes: { ...s.modes, [userId]: stored === 'pro' ? 'pro' : 'simple' } })));
  }, [userId, mode]);
  return mode ?? 'simple';
}

export async function setViewMode(userId: string, mode: ViewMode): Promise<void> {
  useModeStore.setState((s) => ({ modes: { ...s.modes, [userId]: mode } }));
  await secureStorage.setViewMode(userId, mode);
}
