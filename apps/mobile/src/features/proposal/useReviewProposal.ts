import { isApiError } from '@astryx/api-client';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';

import { proposals, scanner } from '@/lib/api';
import { toast } from '@/state/toasts';

import { proposalKey } from './useProposal';

/**
 * "Review proposal": creates the proposal from the signal (or gets the pending one) and opens it (T1, §8.4.3).
 * The scanner re-issues signals every run, so the id on screen can be a run old: on `signal_not_active` the
 * symbol's current signal is looked up and tried once, and the screen's cached signal is refreshed.
 */
export function useReviewProposal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ signalId, symbol }: { signalId: string; symbol: string }) => {
      try {
        return (await proposals.fromSignal(signalId)).data;
      } catch (error) {
        if (!isApiError(error) || error.code !== 'signal_not_active') throw error;
        void queryClient.invalidateQueries({ queryKey: ['signals', 'symbol', symbol] });
        const current = (await scanner.list({ symbol, sort: 'score' }, { limit: 1 })).data.items[0];
        if (!current || current.id === signalId) throw error;
        return (await proposals.fromSignal(current.id)).data;
      }
    },
    onSuccess: (proposal) => {
      queryClient.setQueryData(proposalKey(proposal.id), proposal);
      router.push({ pathname: '/proposal/[id]', params: { id: proposal.id } });
    },
    onError: (error) => {
      toast({ tone: 'warning', title: "Couldn't create the proposal", body: isApiError(error) ? (error.detail ?? error.message) : 'Try again in a moment.' });
    },
  });
}
