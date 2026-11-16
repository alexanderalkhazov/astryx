import { isApiError, type Proposal, type RiskAssessment } from '@astryx/api-client';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { useCallback, useEffect, useRef, useState } from 'react';

import { proposals } from '@/lib/api';

/** Server recompute after the last change (SYSTEM_DESIGN.md §8.4.4). */
export const ASSESS_DEBOUNCE_MS = 150;

export const proposalKey = (id: string) => ['proposal', id] as const;

export interface SizingInputs {
  /** Undefined until the user touches the stepper: the server sizes by default. */
  quantity?: string;
  riskPerTradePct: string;
}

const same = (a: string, b: string) => new Decimal(a).eq(b);

/** Whether an assessment was made for exactly these inputs. */
export function assessedFor(assessment: RiskAssessment, inputs: SizingInputs): boolean {
  return same(assessment.riskPerTradePct, inputs.riskPerTradePct) && (inputs.quantity === undefined || same(assessment.quantity, inputs.quantity));
}

export function useProposal(id: string) {
  return useQuery({ queryKey: proposalKey(id), queryFn: async ({ signal }) => (await proposals.get(id, signal)).data, retry: false });
}

export function useRiskLimits() {
  return useQuery({ queryKey: ['risk-limits'], queryFn: async () => (await proposals.riskLimits()).data, staleTime: 60_000 });
}

/**
 * Sizing state for the proposal screen: every change is re-assessed on the server after 150 ms, cancelling any
 * request still in flight. Only the server's assessment can enable approval.
 */
export function useSizing(proposal: Proposal | undefined) {
  const [inputs, setInputs] = useState<SizingInputs | null>(null);
  const [assessment, setAssessment] = useState<RiskAssessment | null>(null);
  const [assessing, setAssessing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const controller = useRef<AbortController | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const current = assessment ?? proposal?.assessment ?? null;
  const effective: SizingInputs | null = inputs ?? (current ? { riskPerTradePct: current.riskPerTradePct } : null);

  const assess = useCallback(
    (next: SizingInputs) => {
      if (!proposal) return;
      clearTimeout(timer.current);
      controller.current?.abort();
      setAssessing(true);
      setError(null);
      timer.current = setTimeout(() => {
        const mine = new AbortController();
        controller.current = mine;
        proposals
          .assess(proposal.id, next, mine.signal)
          .then((response) => {
            if (controller.current === mine) setAssessment(response.data);
          })
          .catch((e: unknown) => {
            if (controller.current === mine && !mine.signal.aborted) setError(e);
          })
          .finally(() => {
            if (controller.current === mine) setAssessing(false);
          });
      }, ASSESS_DEBOUNCE_MS);
    },
    [proposal],
  );

  useEffect(() => () => {
    clearTimeout(timer.current);
    controller.current?.abort();
  }, []);

  const change = (next: SizingInputs) => {
    setInputs(next);
    assess(next);
  };

  return {
    current,
    inputs: effective,
    assessing,
    error,
    setQuantity: (quantity: string) => effective && change({ ...effective, quantity }),
    setRisk: (riskPerTradePct: string) => effective && change({ ...effective, riskPerTradePct }),
    recheck: () => effective && assess(effective),
  };
}

/** Ticks once a second, so an assessment's 60 s validity shows up without user input. */
export function useNow(): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);
  return now;
}

export type ApproveGate = { enabled: true } | { enabled: false; reason: string; recheck?: boolean };

/**
 * Approve is enabled only when the assessment is for the current inputs, approvable, not stale and unexpired, and
 * no recompute is in flight (§8.4.4).
 */
export function approveGate(args: { proposal: Proposal; current: RiskAssessment | null; inputs: SizingInputs | null; assessing: boolean; error: unknown; now: number }): ApproveGate {
  const { proposal, current, inputs, assessing, error, now } = args;
  if (proposal.status !== 'pending' || Date.parse(proposal.expiresAt) <= now) return { enabled: false, reason: 'This proposal is no longer pending.' };
  if (assessing) return { enabled: false, reason: 'Checking the latest numbers…' };
  if (error) return { enabled: false, reason: "Couldn't verify the checks, so approval is disabled.", recheck: true };
  if (!current || !inputs || !assessedFor(current, inputs)) return { enabled: false, reason: 'Checking the latest numbers…' };
  const failing = current.checks.find((c) => c.status === 'fail');
  // Stale data can pass on a recheck once the feed recovers; other failures need a change of inputs.
  if (failing) return { enabled: false, reason: failing.message, recheck: failing.code === 'data_fresh' || undefined };
  if (current.stale || Date.parse(current.expiresAt) <= now) return { enabled: false, reason: 'The checks are more than a minute old.', recheck: true };
  return current.approvable ? { enabled: true } : { enabled: false, reason: 'A pre-trade check fails.' };
}

export function useRejectProposal(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (reason?: string) => (await proposals.reject(id, reason)).data,
    onSuccess: (proposal) => queryClient.setQueryData(proposalKey(id), proposal),
  });
}

export function isNotFound(error: unknown) {
  return isApiError(error) && error.status === 404;
}
