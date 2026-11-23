import type { Href } from 'expo-router';
import { useEffect } from 'react';
import { create } from 'zustand';

import { secureStorage } from '@/lib/secureStorage';

/** T-195: what this device has seen of the account. Everything else on the checklist comes from the server. */
export interface StartedState {
  /** yyyy-mm-dd, local time, the first day the checklist was shown. */
  firstSeen: string;
  lessonRead: boolean;
  dismissed: boolean;
}

export interface ChecklistItem {
  key: 'plan' | 'watch' | 'trade' | 'lesson' | 'return';
  label: string;
  done: boolean;
  href?: Href;
}

export function checklist(facts: {
  hasPlan: boolean;
  watching: boolean;
  traded: boolean;
  started: StartedState;
  today: string;
}): ChecklistItem[] {
  return [
    { key: 'plan', label: 'Set up a long-term plan', done: facts.hasPlan, href: '/invest' },
    { key: 'watch', label: 'Watch a stock', done: facts.watching, href: '/search' },
    { key: 'trade', label: 'Make a practice trade', done: facts.traded, href: '/guided-trade' },
    { key: 'lesson', label: 'Read one lesson', done: facts.started.lessonRead, href: '/learn' },
    { key: 'return', label: 'Come back another day', done: facts.today > facts.started.firstSeen },
  ];
}

/** Local calendar date, so "another day" means the user's day, not UTC's. */
export const localDate = (now = new Date()) =>
  `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;

const useStartedStore = create<{ states: Record<string, StartedState> }>(() => ({ states: {} }));

async function update(userId: string, change: (s: StartedState) => StartedState) {
  const current = useStartedStore.getState().states[userId] ?? (await read(userId));
  const next = change(current);
  useStartedStore.setState((st) => ({ states: { ...st.states, [userId]: next } }));
  await secureStorage.setStarted(userId, JSON.stringify(next));
}

async function read(userId: string): Promise<StartedState> {
  const stored = await secureStorage.getStarted(userId);
  if (stored) {
    try {
      return JSON.parse(stored) as StartedState;
    } catch {
      // fall through: start again
    }
  }
  return { firstSeen: localDate(), lessonRead: false, dismissed: false };
}

/** The device-side state, created (and saved) on first use. Undefined while loading. */
export function useStarted(userId: string | undefined): StartedState | undefined {
  const state = useStartedStore((s) => (userId ? s.states[userId] : undefined));
  useEffect(() => {
    if (!userId || state) return;
    void read(userId).then((loaded) => update(userId, () => loaded));
  }, [userId, state]);
  return state;
}

export const markLessonRead = (userId: string) => update(userId, (s) => ({ ...s, lessonRead: true }));
export const dismissChecklist = (userId: string) => update(userId, (s) => ({ ...s, dismissed: true }));
