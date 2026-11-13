import { z } from 'zod';

import type { ApiClient } from './client';

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

export const CheckStatus = z.enum(['pass', 'warn', 'fail']);

export const RiskCheck = z.object({ code: z.string(), status: CheckStatus, message: z.string(), actual: Decimal.nullable(), limit: Decimal.nullable() });
export type RiskCheck = z.infer<typeof RiskCheck>;

/** T4 (SYSTEM_DESIGN.md §11.7). `stale` means re-assess before approving (expired, or the inputs changed on the server). */
export const RiskAssessment = z.object({
  id: z.string(),
  proposalId: z.string(),
  inputsHash: z.string(),
  quantity: Decimal,
  sizedBy: z.enum(['risk', 'weight_cap', 'cash', 'user']),
  limits: z.object({ byRisk: Decimal, byWeightCap: Decimal, byCash: Decimal }),
  riskPerTradePct: Decimal,
  quote: z.object({ last: Decimal, asOf: z.string() }),
  equity: Decimal,
  cashAvailable: Decimal,
  capitalRequired: Decimal,
  maxLoss: Decimal,
  maxLossInclCosts: Decimal,
  potentialProfit: Decimal,
  rewardRisk: Decimal,
  estFees: Decimal,
  estSlippage: Decimal,
  impact: z.object({ newWeightPct: Decimal, sector: z.string().nullable(), sectorExposureAfterPct: Decimal, cashAfter: Decimal, openPositionsAfter: z.number().int() }),
  checks: z.array(RiskCheck),
  approvable: z.boolean(),
  createdAt: z.string(),
  expiresAt: z.string(),
  stale: z.boolean(),
});
export type RiskAssessment = z.infer<typeof RiskAssessment>;

/** T2: a research plan, never an order. */
export const Proposal = z.object({
  id: z.string(),
  status: z.enum(['pending', 'approved', 'rejected', 'expired']),
  label: z.string(),
  symbol: z.string(),
  name: z.string(),
  side: z.literal('buy'),
  direction: z.literal('long'),
  orderType: z.enum(['buy_stop', 'limit', 'market']),
  timeInForce: z.literal('day'),
  entry: Decimal,
  stop: Decimal,
  target: Decimal,
  stopPct: Decimal,
  targetPct: Decimal,
  source: z.object({
    type: z.enum(['signal', 'analysis']),
    signalId: z.string().nullable(),
    analysisId: z.string().nullable(),
    strategyId: z.string().nullable(),
    strategyName: z.string().nullable(),
    signalAt: z.string().nullable(),
  }),
  analysis: z.object({
    headline: z.string(),
    summary: z.string(),
    evidence: z.array(z.string()),
    invalidation: z.array(z.string()),
    score: z.string().nullable(),
    copySource: z.enum(['template', 'ai']),
  }),
  createdAt: z.string(),
  expiresAt: z.string(),
  decidedAt: z.string().nullable(),
  rejectReason: z.string().nullable(),
  assessment: RiskAssessment.nullable(),
});
export type Proposal = z.infer<typeof Proposal>;

export const RISK_CHOICES = ['0.0050', '0.0075', '0.0100'] as const;

/** The part of the user's risk settings the proposal screen needs (risk choices above the max are disabled). */
export const RiskLimits = z.object({ maxRiskPerTradePct: Decimal });

/** Trade proposals T1–T5. */
export function proposalsApi(client: ApiClient) {
  return {
    fromSignal: (signalId: string) => client.request('/api/v1/proposals', { method: 'POST', body: { source: 'signal', signalId }, schema: Proposal }),
    fromAnalysis: (analysisId: string) => client.request('/api/v1/proposals', { method: 'POST', body: { source: 'analysis', analysisId }, schema: Proposal }),
    get: (id: string, signal?: AbortSignal) => client.request(`/api/v1/proposals/${encodeURIComponent(id)}`, { schema: Proposal, signal }),
    assess: (id: string, input: { quantity?: string; riskPerTradePct: string }, signal?: AbortSignal) =>
      client.request(`/api/v1/proposals/${encodeURIComponent(id)}/assessments`, { method: 'POST', body: input, schema: RiskAssessment, signal }),
    riskLimits: () => client.request('/api/v1/me/settings/risk', { schema: RiskLimits }),
    reject: (id: string, reason?: string) =>
      client.request(`/api/v1/proposals/${encodeURIComponent(id)}/reject`, { method: 'POST', body: reason ? { reason } : {}, schema: Proposal }),
  };
}
