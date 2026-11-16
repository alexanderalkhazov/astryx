import { z } from 'zod';

import type { ApiClient } from './client';

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

/** OrderDto (SYSTEM_DESIGN.md §11.7). A working entry has no `fill` yet. */
export const PaperOrder = z.object({
  id: z.string(),
  brokerOrderId: z.string(),
  mode: z.literal('paper'),
  broker: z.string(),
  status: z.enum(['accepted', 'working', 'filled', 'cancelled', 'expired', 'rejected']),
  purpose: z.enum(['entry', 'exit_stop', 'exit_target', 'exit_manual', 'exit_reset']),
  symbol: z.string(),
  side: z.enum(['buy', 'sell']),
  orderType: z.enum(['market', 'limit', 'buy_stop', 'sell_stop']),
  quantity: Decimal,
  stopPrice: Decimal.nullable(),
  limitPrice: Decimal.nullable(),
  fill: z
    .object({ price: Decimal, referencePrice: Decimal, slippagePerShare: Decimal, slippage: Decimal, fee: Decimal, filledAt: z.string() })
    .nullable(),
  attached: z.object({ ocoGroupId: z.string(), stop: Decimal, target: Decimal }).nullable(),
  positionId: z.string().nullable(),
  proposalId: z.string().nullable(),
  submittedAt: z.string(),
  expiresAt: z.string().nullable(),
});
export type PaperOrder = z.infer<typeof PaperOrder>;

/** Paper execution T6 (always simulated). Reuse the idempotency key when retrying the same confirm. */
export function paperApi(client: ApiClient) {
  return {
    place: (input: { proposalId: string; assessmentId: string }, idempotencyKey: string) =>
      client.request('/api/v1/paper/orders', { method: 'POST', body: input, idempotencyKey, schema: PaperOrder }),
    working: () => client.request('/api/v1/paper/orders?status=working', { schema: z.object({ items: z.array(PaperOrder) }) }),
    cancel: (orderId: string, idempotencyKey: string) =>
      client.request(`/api/v1/paper/orders/${encodeURIComponent(orderId)}/cancel`, {
        method: 'POST',
        body: {},
        idempotencyKey,
        schema: PaperOrder,
      }),
  };
}
