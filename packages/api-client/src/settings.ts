import { z } from 'zod';

import type { ApiClient } from './client';

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');
const Bounds = z.object({ min: Decimal, max: Decimal });

/** S1 (SYSTEM_DESIGN.md §14.3): fractions (0.0075 = 0.75 %), the version for If-Match, and the editable bounds. */
export const RiskSettings = z.object({
  maxRiskPerTradePct: Decimal,
  maxPositionWeightPct: Decimal,
  maxSectorExposurePct: Decimal,
  sectorWarningPct: Decimal,
  dailyLossHaltPct: Decimal,
  maxOpenPositions: z.number().int(),
  minRewardRisk: Decimal,
  version: z.number().int(),
  bounds: z.object({
    maxRiskPerTradePct: Bounds,
    maxPositionWeightPct: Bounds,
    maxSectorExposurePct: Bounds,
    dailyLossHaltPct: Bounds,
    maxOpenPositions: z.object({ min: z.number().int(), max: z.number().int() }),
    minRewardRisk: Bounds,
  }),
});
export type RiskSettings = z.infer<typeof RiskSettings>;
export type RiskSettingsUpdate = Omit<RiskSettings, 'version' | 'bounds'>;

/** The ETag for a risk settings version (S2 needs it as If-Match). */
export const riskSettingsETag = (settings: Pick<RiskSettings, 'version'>) => `"v${settings.version}"`;

export const NotificationPreferences = z.object({
  fills: z.boolean(),
  signals: z.boolean(),
  stopProximity: z.boolean(),
  strategyGuard: z.boolean(),
  system: z.boolean(),
  dailySummary: z.boolean(),
  pushEnabled: z.boolean(),
});
export type NotificationPreferences = z.infer<typeof NotificationPreferences>;

/** S3. The scanner is shared; these are the user's saved preferences. */
export const ScannerSettings = z.object({
  universe: z.string(),
  minPrice: Decimal,
  minAvgDollarVolume: Decimal,
  minScore: Decimal.nullable(),
  signalTypes: z.array(z.string()),
  universes: z.array(z.object({ code: z.string(), label: z.string() })),
});
export type ScannerSettings = z.infer<typeof ScannerSettings>;

/** Development-only fault injection (§11.11): absent outside Development. */
export const DevFaults = z.object({ marketData: z.enum(['ok', 'down', 'stale', 'bad_tick']), frozenAt: z.string().nullable() });
export type DevFaults = z.infer<typeof DevFaults>;

/** Settings S1–S5, password change, sign out everywhere and the dev fault switch. */
export function settingsApi(client: ApiClient) {
  return {
    risk: () => client.request('/api/v1/me/settings/risk', { schema: RiskSettings }),
    updateRisk: (current: Pick<RiskSettings, 'version'>, body: RiskSettingsUpdate) =>
      client.request('/api/v1/me/settings/risk', { method: 'PUT', body, ifMatch: riskSettingsETag(current), schema: RiskSettings }),
    notifications: () => client.request('/api/v1/me/settings/notifications', { schema: NotificationPreferences }),
    updateNotifications: (body: NotificationPreferences) =>
      client.request('/api/v1/me/settings/notifications', { method: 'PUT', body, schema: NotificationPreferences }),
    scanner: () => client.request('/api/v1/me/settings/scanner', { schema: ScannerSettings }),
    /** S5. `live` is always refused in the MVP: 403 `live_trading_disabled`. */
    setTradingMode: (mode: 'paper' | 'live', confirmation?: 'LIVE') =>
      client.request('/api/v1/me/settings/trading-mode', {
        method: 'PUT',
        body: { mode, confirmation: confirmation ?? null },
        schema: z.object({ mode: z.literal('paper') }),
      }),
    changePassword: (body: { currentPassword: string; newPassword: string }) =>
      client.request('/api/v1/me/change-password', { method: 'POST', body }),
    logoutAll: () => client.request('/api/v1/auth/logout-all', { method: 'POST' }),
    devFaults: () => client.request('/dev/faults', { schema: DevFaults }),
    setDevFaults: (marketData: DevFaults['marketData']) =>
      client.request('/dev/faults', { method: 'POST', body: { marketData }, schema: DevFaults }),
  };
}
