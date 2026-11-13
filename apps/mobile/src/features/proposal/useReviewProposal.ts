import { isApiError } from '@astryx/api-client';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';

import { proposals } from '@/lib/api';
import { toast } from '@/state/toasts';

import { proposalKey } from './useProposal';

/** "Review proposal": creates the proposal from the signal (or gets the pending one) and opens it (T1, §8.4.3). */
export function useReviewProposal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (signalId: string) => (await proposals.fromSignal(signalId)).data,
    onSuccess: (proposal) => {
      queryClient.setQueryData(proposalKey(proposal.id), proposal);
      router.push({ pathname: '/proposal/[id]', params: { id: proposal.id } });
    },
    onError: (error) => {
      toast({ tone: 'warning', title: "Couldn't create the proposal", body: isApiError(error) ? (error.detail ?? error.message) : 'Try again in a moment.' });
    },
  });
}
