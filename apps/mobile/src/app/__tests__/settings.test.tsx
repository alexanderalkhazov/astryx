import { ApiError, type RiskSettings } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import ChangePasswordScreen from '../(app)/settings/change-password';
import SettingsScreen from '../(app)/settings/index';

jest.mock('expo-crypto', () => ({ randomUUID: () => 'reset-key-0001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/session', () => ({ signOut: jest.fn(() => Promise.resolve()) }));
jest.mock('@/lib/api', () => ({
  settings: {
    risk: jest.fn(), updateRisk: jest.fn(), notifications: jest.fn(), updateNotifications: jest.fn(), scanner: jest.fn(), setTradingMode: jest.fn(),
    changePassword: jest.fn(), logoutAll: jest.fn(), devFaults: jest.fn(), setDevFaults: jest.fn(),
  },
  portfolio: { summary: jest.fn(), reset: jest.fn() },
  watchlists: { list: jest.fn() },
}));
type SettingsMock = Record<'risk' | 'updateRisk' | 'notifications' | 'updateNotifications' | 'scanner' | 'setTradingMode' | 'changePassword' | 'logoutAll' | 'devFaults' | 'setDevFaults', jest.Mock>;
const api = jest.requireMock('@/lib/api') as { settings: SettingsMock; portfolio: { summary: jest.Mock; reset: jest.Mock }; watchlists: { list: jest.Mock } };
const { signOut } = jest.requireMock('@/lib/session') as { signOut: jest.Mock };
const ok = <T,>(data: T) => ({ data });

/** Design 18 defaults. */
const RISK: RiskSettings = {
  maxRiskPerTradePct: '0.0075', maxPositionWeightPct: '0.12', maxSectorExposurePct: '0.35', sectorWarningPct: '0.30', dailyLossHaltPct: '0.02', maxOpenPositions: 12, minRewardRisk: '2.0', version: 1,
  bounds: {
    maxRiskPerTradePct: { min: '0.001', max: '0.02' }, maxPositionWeightPct: { min: '0.02', max: '0.25' }, maxSectorExposurePct: { min: '0.10', max: '0.60' },
    dailyLossHaltPct: { min: '0.005', max: '0.05' }, maxOpenPositions: { min: 1, max: 30 }, minRewardRisk: { min: '1.0', max: '5.0' },
  },
};
const PREFS = { fills: true, signals: true, stopProximity: true, strategyGuard: true, system: true, dailySummary: false, pushEnabled: false };

async function renderAt(initialUrl = '/settings') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'settings/index': SettingsScreen,
      'settings/change-password': ChangePasswordScreen,
      'settings/watchlist': () => <Text>Watchlist screen</Text>,
    },
    { initialUrl },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  api.settings.risk.mockResolvedValue(ok(RISK));
  api.settings.notifications.mockResolvedValue(ok(PREFS));
  api.settings.scanner.mockResolvedValue(
    ok({ universe: 'us_large_cap', minPrice: '10.00', minAvgDollarVolume: '20000000', minScore: null, signalTypes: ['breakout', 'momentum', 'pullback', 'unusual_volume', 'earnings'], universes: [{ code: 'us_large_cap', label: 'S&P 500 + Nasdaq 100 + 450' }] }),
  );
  api.settings.devFaults.mockResolvedValue(ok({ marketData: 'ok', frozenAt: null }));
  api.portfolio.summary.mockResolvedValue(ok({ equity: '118172.40', asOf: '2026-11-09T19:36:10Z' }));
  api.watchlists.list.mockResolvedValue(ok({ items: [{ id: 'w1', isDefault: true, items: [{ symbol: 'NVDA' }, { symbol: 'AMD' }] }] }));
});

describe('Settings (designs 18 and 25)', () => {
  it('shows the paper connection and its permissions, never requesting transfers', async () => {
    await renderAt();

    expect(await screen.findByText('Paper account (simulated)')).toBeOnTheScreen();
    expect(screen.getByText('PAPER')).toBeOnTheScreen();
    expect(await screen.findByText('Connected · synced 14:36 ET')).toBeOnTheScreen();
    expect(screen.getByText('Paper only')).toBeOnTheScreen();
    expect(screen.getByText('Never requested')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Connect eToro' })).toBeDisabled();
  });

  it('walks the live guard to "isn\'t available yet" and stays in paper', async () => {
    api.settings.setTradingMode.mockRejectedValue(new ApiError({ status: 403, code: 'live_trading_disabled', detail: "Live trading isn't available yet. Your account stays in paper mode." }));
    await renderAt();
    await screen.findByText('Max risk per trade');

    await fireEvent.press(screen.getByRole('radio', { name: 'Live' }));

    expect(await screen.findByText('Switch to live trading?')).toBeOnTheScreen();
    expect(screen.getByLabelText('Max risk per trade: 0.75%')).toBeOnTheScreen();
    expect(screen.getByLabelText('Daily loss halt: −2.00%')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByRole('button', { name: 'Switch to live' })).toBeDisabled();
    await fireEvent.changeText(screen.getByLabelText('Type LIVE to confirm'), 'LIVE');
    await fireEvent.press(screen.getByRole('button', { name: 'Switch to live' }));

    expect(await screen.findByText("Live trading isn't available yet")).toBeOnTheScreen();
    expect(api.settings.setTradingMode).toHaveBeenCalledWith('live', 'LIVE');
    await fireEvent.press(screen.getByRole('button', { name: 'Stay in paper mode' }));
    await waitFor(() => expect(screen.queryByTestId('live-guard')).toBeNull());
    expect(screen.getByRole('radio', { name: 'Paper', selected: true })).toBeOnTheScreen();
  });

  it('edits risk limits within bounds and saves them with the version', async () => {
    api.settings.updateRisk.mockResolvedValue(ok({ ...RISK, maxRiskPerTradePct: '0.008', version: 2 }));
    await renderAt();
    await screen.findByText('Max risk per trade');
    expect(screen.getByRole('button', { name: 'Save limits' })).toBeDisabled();
    expect(screen.getByText('0.1–2%')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Increase Max risk per trade' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Save limits' }));

    await waitFor(() => expect(api.settings.updateRisk).toHaveBeenCalledWith(expect.objectContaining({ version: 1 }), expect.objectContaining({ maxRiskPerTradePct: '0.008', maxOpenPositions: 12, sectorWarningPct: '0.30' })));
  });

  it('keeps the upper bound out of reach', async () => {
    api.settings.risk.mockResolvedValue(ok({ ...RISK, maxOpenPositions: 30 }));
    await renderAt();
    await screen.findByText('Max open positions');

    expect(screen.getByRole('button', { name: 'Increase Max open positions' })).toBeDisabled();
  });

  it('shows the shared scanner settings and toggles notification categories', async () => {
    api.settings.updateNotifications.mockResolvedValue(ok({ ...PREFS, strategyGuard: false }));
    await renderAt();

    expect(await screen.findByText('S&P 500 + Nasdaq 100 + 450')).toBeOnTheScreen();
    expect(screen.getByText('$10 · $20M')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Watchlist, 2 stocks, Manage')).toBeOnTheScreen();
    await fireEvent.press(await screen.findByLabelText('Strategy guard'));

    await waitFor(() => expect(api.settings.updateNotifications).toHaveBeenCalledWith({ ...PREFS, strategyGuard: false }));
  });

  it('signs out of all devices after confirming', async () => {
    api.settings.logoutAll.mockResolvedValue(ok(undefined));
    await renderAt();

    await fireEvent.press(await screen.findByRole('button', { name: 'Sign out of all devices' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Sign out all' }));

    await waitFor(() => expect(signOut).toHaveBeenCalled());
    expect(api.settings.logoutAll).toHaveBeenCalled();
  });

  it('shows prototype controls in dev builds and switches the market data fault', async () => {
    api.settings.setDevFaults.mockResolvedValue(ok({ marketData: 'down', frozenAt: null }));
    await renderAt();

    expect(await screen.findByTestId('prototype-controls')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('radio', { name: 'down' }));

    await waitFor(() => expect(api.settings.setDevFaults).toHaveBeenCalledWith('down'));
  });
});

describe('Paper account reset (P11, proposed dialog)', () => {
  it('needs RESET typed, then resets once with one key and reports what it did', async () => {
    api.portfolio.reset.mockResolvedValue(ok({ portfolioId: 'p1', cashBalance: '100000.00', startingCash: '100000.00', closedPositions: 2, cancelledOrders: 4, resetCount: 1, epochStartedAt: '2026-11-13T05:00:00Z' }));
    await renderAt();

    await fireEvent.press(await screen.findByRole('button', { name: 'Reset paper account' }));
    expect(screen.getByRole('button', { name: 'Reset' })).toBeDisabled();
    await fireEvent.changeText(screen.getByLabelText('Type RESET to confirm'), 'RESET');
    await fireEvent.press(screen.getByRole('button', { name: 'Reset' }));

    await waitFor(() => expect(api.portfolio.reset).toHaveBeenCalledWith('reset-key-0001'));
    await waitFor(() => expect(screen.queryByTestId('reset-dialog')).toBeNull());
  });
});

describe('Change password', () => {
  it('shows the server message for a wrong current password', async () => {
    api.settings.changePassword.mockRejectedValue(new ApiError({ status: 422, code: 'current_password_incorrect', detail: 'The current password is incorrect.' }));
    await renderAt('/settings/change-password');

    await fireEvent.changeText(await screen.findByLabelText('Current password'), 'old password here');
    await fireEvent.changeText(screen.getByLabelText('New password'), 'a brand new passphrase');
    await fireEvent.changeText(screen.getByLabelText('Confirm new password'), 'a brand new passphrase');
    await fireEvent.press(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('The current password is incorrect.')).toBeOnTheScreen();
    expect(api.settings.changePassword).toHaveBeenCalledWith({ currentPassword: 'old password here', newPassword: 'a brand new passphrase' });
  });
});
