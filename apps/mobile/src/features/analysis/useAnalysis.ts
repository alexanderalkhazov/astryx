import { type AnalysisJob, isApiError } from '@astryx/api-client';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { randomUUID } from 'expo-crypto';
import { create } from 'zustand';

import { useRealtime } from '@/features/realtime/realtime';
import { analyses } from '@/lib/api';

import { analysisKeys } from './keys';

const TERMINAL = new Set<AnalysisJob['status']>(['completed', 'failed', 'cancelled']);

/** While the socket is up, `analysis.updated` drives refetches; this slow poll only covers a missed event. */
export const SAFETY_POLL_MS = 15_000;

export const isTerminal = (job: AnalysisJob | undefined) => job !== undefined && TERMINAL.has(job.status);

/** The job each symbol is waiting on in this app session, so leaving the screen and coming back resumes progress. */
export const useAnalysisJobs = create<{ bySymbol: Record<string, string>; track: (symbol: string, jobId: string) => void }>((set) => ({
  bySymbol: {},
  track: (symbol, jobId) => set((s) => ({ bySymbol: { ...s.bySymbol, [symbol]: jobId } })),
}));

/** The symbol's latest usable analysis (AI5 with limit 1, then AI4), or null when there is none. */
export function useLatestAnalysis(symbol: string) {
  return useQuery({
    queryKey: analysisKeys.list(symbol),
    queryFn: async () => {
      const top = (await analyses.list({ symbol, limit: 1 })).data.items[0];
      return top ? (await analyses.get(top.id)).data : null;
    },
    retry: false,
  });
}

/** AI2 for the tracked job: polled at `pollAfterMs` when the socket is down, slowly when it is up, never once finished. */
export function useAnalysisJob(jobId: string | undefined) {
  const connected = useRealtime((s) => s.connected);
  return useQuery({
    queryKey: analysisKeys.job(jobId ?? ''),
    queryFn: async () => (await analyses.job(jobId!)).data,
    enabled: jobId !== undefined,
    refetchInterval: (query) => {
      const job = query.state.data;
      if (!job || isTerminal(job)) return false;
      return connected ? SAFETY_POLL_MS : (job.pollAfterMs ?? 2_000);
    },
  });
}

/**
 * AI1 for "Analyze with AI". One idempotency key per tap. A 409 `duplicate` means a job for this symbol is already
 * running (another device, or an earlier tap), so that job is followed instead.
 */
export function useRequestAnalysis(symbol: string) {
  const queryClient = useQueryClient();
  const track = useAnalysisJobs((s) => s.track);
  return useMutation({
    mutationFn: async () => {
      try {
        return (await analyses.create(symbol, randomUUID())).data;
      } catch (error) {
        const running = isApiError(error) && error.code === 'duplicate' ? error.extensions.jobId : undefined;
        if (typeof running === 'string') return (await analyses.job(running)).data;
        throw error;
      }
    },
    onSuccess: (job) => {
      queryClient.setQueryData(analysisKeys.job(job.id), job);
      track(symbol, job.id);
    },
  });
}

export function useCancelAnalysis() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (jobId: string) => (await analyses.cancel(jobId)).data,
    onSuccess: (job) => queryClient.setQueryData(analysisKeys.job(job.id), job),
  });
}
