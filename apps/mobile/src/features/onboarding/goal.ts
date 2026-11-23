import type { Href } from 'expo-router';
import { useEffect } from 'react';
import { create } from 'zustand';

import { secureStorage } from '@/lib/secureStorage';

/** T-190: what the user came to do, asked once after the onboarding questions. It shapes Home (T-191). */
export type Goal = 'invest' | 'practice' | 'explore';

export const GOALS: readonly { value: Goal; title: string; body: string; href: Href }[] = [
  {
    value: 'invest',
    title: 'Grow my money for the long term',
    body: 'Set up a simple plan of index funds that buys a little every week or month.',
    href: '/invest',
  },
  {
    value: 'practice',
    title: 'Practice trading with pretend money',
    body: 'Try buying and selling single stocks. Nothing here can cost you real money.',
    href: '/discover',
  },
  {
    value: 'explore',
    title: 'Just look around',
    body: 'Browse stocks and ideas first. You can choose a goal later in Settings.',
    href: '/discover',
  },
];

export const GOAL_LABEL: Record<Goal, string> = {
  invest: 'Grow money long term',
  practice: 'Practice trading',
  explore: 'Look around',
};

const isGoal = (value: string | null): value is Goal => GOALS.some((g) => g.value === value);

const useGoalStore = create<{ goals: Record<string, Goal | null> }>(() => ({ goals: {} }));

/** The account's goal on this device: null when not chosen yet, undefined while loading or signed out. */
export function useGoal(userId: string | undefined): Goal | null | undefined {
  const goal = useGoalStore((s) => (userId ? s.goals[userId] : undefined));
  useEffect(() => {
    if (!userId || goal !== undefined) return;
    void Promise.resolve(secureStorage.getGoal(userId)).then((stored) =>
      useGoalStore.setState((s) => ({ goals: { ...s.goals, [userId]: isGoal(stored ?? null) ? (stored as Goal) : null } })),
    );
  }, [userId, goal]);
  return goal;
}

export async function saveGoal(userId: string, goal: Goal): Promise<void> {
  await secureStorage.setGoal(userId, goal);
  useGoalStore.setState((s) => ({ goals: { ...s.goals, [userId]: goal } }));
}
