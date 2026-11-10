// Typed client for the Astryx API (SYSTEM_DESIGN.md §8.8).
// Types will be generated from apps/api/openapi.json in Phase 1; this file holds the hand-written primitives.
import { z } from 'zod';

/** Money, prices, quantities and percents travel as decimal strings — never JS numbers. */
export const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');
export type Decimal = z.infer<typeof Decimal>;

export const SystemStatus = z.object({
  database: z.string(),
  ai: z.object({ status: z.string(), model: z.string().nullable() }),
  marketData: z.object({ provider: z.string().nullable() }),
  broker: z.object({ adapter: z.string().nullable(), mode: z.literal('paper') }),
  features: z.object({ liveTrading: z.literal(false) }),
});
export type SystemStatus = z.infer<typeof SystemStatus>;

export async function getSystemStatus(baseUrl: string, signal?: AbortSignal): Promise<SystemStatus> {
  const response = await fetch(`${baseUrl}/api/v1/system/status`, signal ? { signal } : {});
  if (!response.ok) throw new Error(`System status failed: HTTP ${response.status}`);
  return SystemStatus.parse(await response.json());
}
