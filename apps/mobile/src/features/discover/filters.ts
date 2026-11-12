import type { SignalFilters, SignalType } from '@astryx/api-client';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { create } from 'zustand';

export type Sort = NonNullable<SignalFilters['sort']>;

/** What the Discover screen filters by (design 03). `type` is the chip row; the rest live in the Filters sheet. */
export interface DiscoverFilters {
  type?: SignalType;
  sector?: string;
  marketCap?: 'mega' | 'large';
  horizon?: 'intraday' | 'swing' | 'position';
  minScore?: 70 | 80;
  sort: Sort;
}

export const DEFAULT_FILTERS: DiscoverFilters = { sort: 'score' };

/** Sector chips: label → the sector name the API filters on. */
export const SECTORS: readonly { label: string; value: string }[] = [
  { label: 'Technology', value: 'Information Technology' },
  { label: 'Staples', value: 'Consumer Staples' },
  { label: 'Industrials', value: 'Industrials' },
];

export const MARKET_CAPS = [
  { label: 'Mega · >$200B', value: 'mega' },
  { label: 'Large · $10–200B', value: 'large' },
] as const;

export const HORIZONS = [
  { label: 'Intraday', value: 'intraday' },
  { label: 'Swing · 2–6 wk', value: 'swing' },
  { label: 'Position · 1–3 mo', value: 'position' },
] as const;

export const MIN_SCORES = [70, 80] as const;

export const SORTS: readonly { label: string; value: Sort }[] = [
  { label: 'Score', value: 'score' },
  { label: 'Risk/reward', value: 'rr' },
  { label: 'Rel. strength', value: 'relStrength' },
  { label: 'Day change', value: 'change' },
];

/** Filters other than the type chip and the sort, as removable chips under the type row (design 21). */
export function activeFilterChips(f: DiscoverFilters): { key: keyof DiscoverFilters; label: string }[] {
  const chips: { key: keyof DiscoverFilters; label: string }[] = [];
  if (f.sector) chips.push({ key: 'sector', label: SECTORS.find((s) => s.value === f.sector)?.label ?? f.sector });
  if (f.marketCap) chips.push({ key: 'marketCap', label: MARKET_CAPS.find((m) => m.value === f.marketCap)!.label.split(' · ')[0]! });
  if (f.horizon) chips.push({ key: 'horizon', label: HORIZONS.find((h) => h.value === f.horizon)!.label.split(' · ')[0]! });
  if (f.minScore) chips.push({ key: 'minScore', label: `Score ${f.minScore}+` });
  return chips;
}

export const toQuery = (f: DiscoverFilters): SignalFilters => ({
  type: f.type,
  sector: f.sector,
  marketCap: f.marketCap,
  horizon: f.horizon,
  minScore: f.minScore,
  sort: f.sort,
});

const storageKey = (userId: string) => `discover-filters:${userId}`;

interface FiltersState {
  filters: DiscoverFilters;
  /** Whose filters these are; null until loaded. */
  userId: string | null;
  set: (patch: Partial<DiscoverFilters>) => void;
  remove: (key: keyof DiscoverFilters) => void;
  reset: () => void;
  /** Loads the user's saved filters (persisted per user on this device). */
  load: (userId: string) => Promise<void>;
}

function save(userId: string | null, filters: DiscoverFilters) {
  if (!userId) return;
  void AsyncStorage.setItem(storageKey(userId), JSON.stringify(filters)).catch(() => undefined);
}

export const useDiscoverFilters = create<FiltersState>((set, get) => ({
  filters: DEFAULT_FILTERS,
  userId: null,
  set: (patch) => {
    const filters = { ...get().filters, ...patch };
    set({ filters });
    save(get().userId, filters);
  },
  remove: (key) => {
    const filters = { ...get().filters, [key]: key === 'sort' ? 'score' : undefined };
    set({ filters });
    save(get().userId, filters);
  },
  reset: () => {
    set({ filters: DEFAULT_FILTERS });
    save(get().userId, DEFAULT_FILTERS);
  },
  load: async (userId) => {
    if (get().userId === userId) return;
    let filters = DEFAULT_FILTERS;
    try {
      const raw = await AsyncStorage.getItem(storageKey(userId));
      if (raw) filters = { ...DEFAULT_FILTERS, ...(JSON.parse(raw) as Partial<DiscoverFilters>) };
    } catch {
      // A broken or missing entry falls back to the defaults.
    }
    set({ filters, userId });
  },
}));
