import type { Bars } from '@astryx/api-client';
import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { market } from '@/lib/api';

export type Timeframe = '1D' | '5D' | '1M' | '3M' | '1Y';

export const TIMEFRAMES: readonly { value: Timeframe; label: string }[] = [
  { value: '1D', label: '1D' },
  { value: '5D', label: '5D' },
  { value: '1M', label: '1M' },
  { value: '3M', label: '3M' },
  { value: '1Y', label: '1Y' },
];

const MONTHS: Partial<Record<Timeframe, number>> = { '1M': 1, '3M': 3, '1Y': 12 };

/** MA50 needs 50 sessions before the first drawn bar; 4 calendar months covers them with holidays to spare. */
const WARMUP_MONTHS = 4;

/** Stale times (SYSTEM_DESIGN.md §8.7): quotes 15 s, instrument details 24 h. */
const QUOTE_STALE_MS = 15_000;
const DAY_MS = 24 * 60 * 60 * 1000;

const isoDate = (date: Date) => date.toISOString().slice(0, 10);

function monthsBefore(isoDay: string, months: number): string {
  const date = new Date(`${isoDay}T12:00:00Z`);
  date.setUTCMonth(date.getUTCMonth() - months);
  return isoDate(date);
}

export interface ChartData extends Bars {
  /** Leading bars that only warm up the moving averages. */
  warmup: number;
}

/**
 * Bars for a timeframe. Daily timeframes fetch extra history so MA20/MA50 start at the left edge; the cut-off mirrors
 * the server's (latest session minus N months). Intraday timeframes use the server's own range.
 */
async function fetchChart(symbol: string, timeframe: Timeframe): Promise<ChartData> {
  const months = MONTHS[timeframe];
  if (months === undefined) {
    const { data } = await market.bars(symbol, { timeframe });
    return { ...data, warmup: 0 };
  }

  const from = monthsBefore(isoDate(new Date()), months + WARMUP_MONTHS);
  const { data } = await market.bars(symbol, { interval: '1d', from });
  const last = data.bars.at(-1);
  if (!last) return { ...data, warmup: 0 };
  const cutoff = monthsBefore(last.d, months);
  const warmup = data.bars.findIndex((b) => b.d >= cutoff);
  return { ...data, warmup: warmup < 0 ? 0 : warmup };
}

export function useStock(symbol: string, timeframe: Timeframe) {
  const instrument = useQuery({
    queryKey: ['stock', symbol, 'instrument'],
    queryFn: async () => (await market.instrument(symbol)).data,
    staleTime: DAY_MS,
  });
  const quote = useQuery({
    queryKey: ['stock', symbol, 'quote'],
    queryFn: async () => (await market.quote(symbol)).data,
    staleTime: QUOTE_STALE_MS,
    refetchInterval: QUOTE_STALE_MS,
    enabled: instrument.isSuccess,
  });
  const chart = useQuery({
    queryKey: ['stock', symbol, 'bars', timeframe],
    queryFn: () => fetchChart(symbol, timeframe),
    placeholderData: keepPreviousData,
    enabled: instrument.isSuccess,
  });
  return { instrument, quote, chart };
}

export function useFundamentals(symbol: string, enabled: boolean) {
  return useQuery({ queryKey: ['stock', symbol, 'fundamentals'], queryFn: async () => (await market.fundamentals(symbol)).data, staleTime: DAY_MS, enabled });
}

export function useNews(symbol: string, enabled: boolean) {
  return useQuery({ queryKey: ['stock', symbol, 'news'], queryFn: async () => (await market.news(symbol, 10)).data.items, enabled });
}
