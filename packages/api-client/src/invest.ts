import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

type Schemas = components['schemas'];

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

export const INVEST_PROFILES = ['cautious', 'balanced', 'growth'] as const;
export type InvestProfile = (typeof INVEST_PROFILES)[number];
export const CONTRIBUTION_FREQUENCIES = ['none', 'weekly', 'monthly'] as const;
export type ContributionFrequency = (typeof CONTRIBUTION_FREQUENCIES)[number];

export const InvestFund = z.object({
  symbol: z.string(),
  name: z.string(),
  role: z.string(),
  targetWeight: Decimal,
  quantity: Decimal,
  price: Decimal.nullable(),
  value: Decimal,
  weight: Decimal,
  costBasis: Decimal,
}) satisfies z.ZodType<Schemas['InvestFundDto']>;
export type InvestFund = z.infer<typeof InvestFund>;

/** I1 (T-164): the long-term plan valued at the latest prices. */
export const InvestPlan = z.object({
  profile: z.enum(INVEST_PROFILES),
  value: Decimal,
  cash: Decimal,
  contributed: Decimal,
  gain: Decimal,
  gainPct: Decimal,
  funds: z.array(InvestFund),
  maxDrift: Decimal,
  rebalanceSuggested: z.boolean(),
  contribution: z.object({ amount: Decimal, frequency: z.enum(CONTRIBUTION_FREQUENCIES), nextOn: z.string().nullable() }),
  goal: z.object({ amount: Decimal, date: z.string().nullable(), progress: Decimal }).nullable(),
  activity: z.array(
    z.object({
      kind: z.enum(['deposit', 'buy', 'sell']),
      symbol: z.string().nullable(),
      quantity: Decimal.nullable(),
      price: Decimal.nullable(),
      amount: Decimal,
      occurredAt: z.string(),
    }),
  ),
  pricesAsOf: z.string().nullable(),
}) satisfies z.ZodType<Schemas['InvestDto']>;
export type InvestPlan = z.infer<typeof InvestPlan>;

export interface SaveInvestPlan {
  profile: InvestProfile;
  /** Only used when creating the plan. */
  initialDeposit?: string;
  contributionAmount: string;
  contributionFrequency: ContributionFrequency;
  goalAmount?: string | null;
  goalDate?: string | null;
}

/** I1–I4 (T-164). */
export function investApi(client: ApiClient) {
  return {
    get: () => client.request('/api/v1/invest', { schema: InvestPlan }),
    save: (body: SaveInvestPlan, idempotencyKey: string) =>
      client.request('/api/v1/invest', { method: 'PUT', body, idempotencyKey, schema: InvestPlan }),
    investCash: (idempotencyKey: string) =>
      client.request('/api/v1/invest/invest-cash', { method: 'POST', body: {}, idempotencyKey, schema: InvestPlan }),
    rebalance: (idempotencyKey: string) =>
      client.request('/api/v1/invest/rebalance', { method: 'POST', body: {}, idempotencyKey, schema: InvestPlan }),
  };
}
