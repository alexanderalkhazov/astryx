import type { RiskSettingsUpdate } from '@astryx/api-client';
import { useEffect } from 'react';
import { create } from 'zustand';

import { secureStorage } from '@/lib/secureStorage';

/** T-162: who the user is as an investor, from three onboarding questions. */
export type ExperienceLevel = 'beginner' | 'intermediate' | 'experienced';
export type Horizon = 'years' | 'months' | 'weeks';
export type LossTolerance = 'sell' | 'hold' | 'normal';

export interface InvestorProfile {
  level: ExperienceLevel;
  horizon: Horizon;
  tolerance: LossTolerance;
}

export const QUESTIONS = {
  experience: {
    title: 'How much have you invested before?',
    options: [
      { value: 'beginner', label: "I've never bought a stock" },
      { value: 'intermediate', label: "I've bought a few, but don't trade" },
      { value: 'experienced', label: 'I trade regularly' },
    ],
  },
  horizon: {
    title: 'How long do you want to keep money invested?',
    options: [
      { value: 'years', label: 'Years: saving for the long term' },
      { value: 'months', label: 'Months' },
      { value: 'weeks', label: 'Days to weeks: active trading' },
    ],
  },
  tolerance: {
    title: 'If $10,000 fell to $9,000 in a month, you would…',
    options: [
      { value: 'sell', label: 'Sell, I can’t stand losing money' },
      { value: 'hold', label: 'Feel uneasy but hold on' },
      { value: 'normal', label: 'See it as normal ups and downs' },
    ],
  },
} as const;

/**
 * The level the answers support: experience sets the ceiling, and someone who would sell after a 10 % drop is treated
 * one level more cautiously (never above Beginner if they have also never invested).
 */
export function levelFor(experience: ExperienceLevel, tolerance: LossTolerance): ExperienceLevel {
  if (tolerance !== 'sell') return experience;
  return experience === 'experienced' ? 'intermediate' : 'beginner';
}

/** Risk limits per level (§14.4 defaults are Experienced). Beginners risk less per trade and hold fewer positions. */
export const LIMITS: Record<ExperienceLevel, RiskSettingsUpdate> = {
  beginner: {
    maxRiskPerTradePct: '0.005',
    maxPositionWeightPct: '0.08',
    maxSectorExposurePct: '0.25',
    sectorWarningPct: '0.20',
    dailyLossHaltPct: '0.015',
    maxOpenPositions: 5,
    minRewardRisk: '2.0',
  },
  intermediate: {
    maxRiskPerTradePct: '0.0075',
    maxPositionWeightPct: '0.10',
    maxSectorExposurePct: '0.30',
    sectorWarningPct: '0.25',
    dailyLossHaltPct: '0.02',
    maxOpenPositions: 8,
    minRewardRisk: '1.75',
  },
  experienced: {
    maxRiskPerTradePct: '0.0075',
    maxPositionWeightPct: '0.12',
    maxSectorExposurePct: '0.35',
    sectorWarningPct: '0.30',
    dailyLossHaltPct: '0.02',
    maxOpenPositions: 12,
    minRewardRisk: '1.5',
  },
};

export const LEVEL_LABEL: Record<ExperienceLevel, string> = {
  beginner: 'Beginner',
  intermediate: 'Intermediate',
  experienced: 'Experienced',
};

/** What each level unlocks: Beginners keep the engine's position size. */
export const canChooseQuantity = (level: ExperienceLevel | undefined) => level !== 'beginner';

const useProfileStore = create<{ profiles: Record<string, InvestorProfile | null> }>(() => ({ profiles: {} }));

/** The account's profile on this device: null when not answered yet, undefined while loading or signed out. */
export function useInvestorProfile(userId: string | undefined): InvestorProfile | null | undefined {
  const profile = useProfileStore((s) => (userId ? s.profiles[userId] : undefined));
  useEffect(() => {
    if (!userId || profile !== undefined) return;
    void secureStorage.getProfile(userId).then((stored) =>
      useProfileStore.setState((s) => ({
        profiles: { ...s.profiles, [userId]: stored ? (JSON.parse(stored) as InvestorProfile) : null },
      })),
    );
  }, [userId, profile]);
  return profile;
}

export async function saveInvestorProfile(userId: string, profile: InvestorProfile): Promise<void> {
  await secureStorage.setProfile(userId, JSON.stringify(profile));
  useProfileStore.setState((s) => ({ profiles: { ...s.profiles, [userId]: profile } }));
}
