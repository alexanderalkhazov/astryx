import { z } from 'zod';

import type { ApiClient } from './client';

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

/** N7 item (SYSTEM_DESIGN.md §17.8): an Alpaca paper connection. The secret is never returned. */
export const BrokerConnection = z.object({
  id: z.string(),
  broker: z.literal('alpaca'),
  environment: z.literal('paper'),
  label: z.string(),
  keySuffix: z.string(),
  accountRef: z.string().nullable(),
  status: z.enum(['connected', 'degraded', 'revoked']),
  permissions: z.object({ readPortfolio: z.boolean(), placeOrders: z.string(), depositsWithdrawals: z.string() }),
  connectedAt: z.string(),
  lastSyncAt: z.string().nullable(),
  lastError: z.string().nullable(),
  /** Cash plus margin at Alpaca, for information: Astryx trades with cash only. */
  buyingPower: Decimal.nullable().optional(),
});
export type BrokerConnection = z.infer<typeof BrokerConnection>;

/** N7–N9 (T-141): connect an Alpaca paper account with its key ID and secret, list it, disconnect it. */
export function brokerApi(client: ApiClient) {
  return {
    connections: () => client.request('/api/v1/broker/connections', { schema: z.object({ items: z.array(BrokerConnection) }) }),
    connect: (input: { keyId: string; secretKey: string }) =>
      client.request('/api/v1/broker/connections', {
        method: 'POST',
        body: { broker: 'alpaca', environment: 'paper', ...input },
        schema: BrokerConnection,
      }),
    disconnect: (id: string) => client.request(`/api/v1/broker/connections/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  };
}
