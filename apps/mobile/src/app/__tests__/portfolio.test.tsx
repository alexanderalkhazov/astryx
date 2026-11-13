import type { PortfolioPosition } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import PortfolioScreen from '../(app)/(tabs)/portfolio';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({ portfolio: { summary: jest.fn(), positions: jest.fn(), allocation: jest.fn(), exposure: jest.fn(), performance: jest.fn(), trades: jest.fn() } }));
const api = (jest.requireMock('@/lib/api') as { portfolio: Record<'summary' | 'positions' | 'allocation' | 'exposure' | 'performance' | 'trades', jest.Mock> }).portfolio;
const ok = <T,>(data: T) => ({ data });

/** Design 01/09 values. */
const SUMMARY = {
  id: 'pf', mode: 'paper', currency: 'USD', equity: '118172.18', cash: '7665.03', cashAvailable: '7665.03', invested: '110507.15', unrealizedPnl: '8460.13',
  dayPnl: '484.13', dayPnlPct: '0.0041', allTimePnl: '10672.93', allTimeReturnPct: '0.0993', realizedPnl: '2212.80', openPositions: 2, halted: false,
  asOf: '2026-11-09T19:36:10Z', stale: false,
};

const position = (over: Partial<PortfolioPosition>): PortfolioPosition => ({
  id: 'x', symbol: 'NVDA', name: 'NVIDIA Corp.', status: 'open', quantity: '76', avgEntryPrice: '186.52', last: '186.40', marketValue: '14166.40', unrealizedPnl: '-9.12', unrealizedPct: '-0.0006',
  weightPct: '0.1199', weightCapPct: '0.1200', overCap: false, stopPrice: '178.20', targetPrice: '204.00', stopDistancePct: '0.0440', nearStop: false, isNew: true,
  openedAt: '2026-11-09T19:36:08Z', exitPrice: null, exitReason: null, realizedPnl: null, closedAt: null, ...over,
});

async function renderPortfolio() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      portfolio: PortfolioScreen,
      'position/[ticker]': () => <Text>Position screen</Text>,
      discover: () => <Text>Discover screen</Text>,
    },
    { initialUrl: '/portfolio' },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  api.summary.mockResolvedValue(ok(SUMMARY));
  api.positions.mockResolvedValue(ok({ items: [position({}), position({ id: 'y', symbol: 'JPM', name: 'JPMorgan', weightPct: '0.1500', overCap: true, isNew: false, nearStop: true, stopDistancePct: '0.0120', unrealizedPnl: '2100.00', unrealizedPct: '0.1200' })], nextCursor: null }));
  api.allocation.mockResolvedValue(ok({ sectors: [{ sector: 'Semiconductors', value: '28480.00', pct: '0.2410', warning: false, overLimit: false }, { sector: 'Banks', value: '38000.00', pct: '0.3216', warning: true, overLimit: false }], assets: [{ asset: 'stocks', value: '110507.15', pct: '0.9351' }, { asset: 'cash', value: '7665.03', pct: '0.0649' }], sectorWarningPct: '0.3000', maxSectorExposurePct: '0.3500' }));
  api.exposure.mockResolvedValue(ok({ grossPct: '0.9351', betaWeightedPct: null, openRiskToStops: '4210.40', concentrationWarnings: [{ kind: 'position_weight', subject: 'JPM', pct: '0.1500', limit: '0.1200', message: 'JPM is 15.0% of the portfolio, above the 12.0% cap. It is not sold automatically.' }] }));
  api.performance.mockResolvedValue(ok({ range: '1M', benchmark: 'SPY', series: [{ t: '2026-10-10', equity: '112410.22', benchmark: '112410.22' }, { t: '2026-11-09', equity: '118172.18', benchmark: '114770.83' }], metrics: { twrPct: '0.0512', benchmarkPct: '0.0210', maxDrawdownPct: '-0.0340', winRate: '0.58', avgWin: '812.20', avgLoss: '-402.75', trades: 19 } }));
  api.trades.mockResolvedValue(ok({ items: [{ positionId: 't1', symbol: 'AMD', quantity: '50', entryPrice: '150.00', exitPrice: '160.00', exitReason: 'target', realizedPnl: '500.00', returnPct: '0.0667', openedAt: '2026-10-01T15:00:00Z', closedAt: '2026-10-08T15:00:00Z', holdingDays: 7 }], nextCursor: null }));
});

describe('Portfolio (designs 09 and 15)', () => {
  it('shows the hero, the metric row and positions against the 12% cap', async () => {
    await renderPortfolio();

    expect(await screen.findByLabelText('Portfolio value: 118172 dollars and 18 cents')).toBeOnTheScreen();
    expect(screen.getByTestId('paper-chip')).toBeOnTheScreen();
    expect(screen.getByText('+$484.13 (+0.41%) today')).toBeOnTheScreen();
    expect(screen.getByText('+$10,672.93 (+9.93%) all time')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Invested: $110,507.15')).toBeOnTheScreen();
    expect(screen.getByLabelText('Weight 11.99% of a 12% cap')).toBeOnTheScreen();
    expect(screen.getByLabelText('Weight 15.00% of a 12% cap, over the cap')).toBeOnTheScreen();
    expect(screen.getByText('NEW')).toBeOnTheScreen();
    expect(screen.getByText('1.2% TO STOP')).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('position-NVDA'));
    await waitFor(() => expect(screen).toHavePathname('/position/NVDA'));
  });

  it('explains a reconciliation halt differently from the daily loss halt', async () => {
    api.summary.mockResolvedValue(ok({ ...SUMMARY, halted: true, haltReason: 'reconciliation' }));
    await renderPortfolio();

    expect(await screen.findByText(/nightly check found a mismatch/)).toBeOnTheScreen();
  });

  it('offers a way to find a setup when nothing is open', async () => {
    api.positions.mockResolvedValue(ok({ items: [], nextCursor: null }));
    await renderPortfolio();

    expect(await screen.findByText('No open positions')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Find a setup' }));
    await waitFor(() => expect(screen).toHavePathname('/discover'));
  });

  it('shows sectors with the near-limit warning, the asset donut, exposure and concentration', async () => {
    await renderPortfolio();
    await screen.findByTestId('positions-tab');

    await fireEvent.press(screen.getByRole('tab', { name: 'Allocation' }));

    expect(await screen.findByLabelText('Banks: 32.2%, $38,000.00, near the 35% limit')).toBeOnTheScreen();
    expect(screen.getByText('Near limit 35%')).toBeOnTheScreen();
    expect(screen.getByLabelText('US equities 93.5%, Cash 6.5%')).toBeOnTheScreen();
    expect(screen.getByLabelText('Beta-weighted: —')).toBeOnTheScreen();
    expect(screen.getByLabelText('Open risk to stops: $4,210.40')).toBeOnTheScreen();
    expect(screen.getByText('JPM is 15.0% of the portfolio, above the 12.0% cap. It is not sold automatically.')).toBeOnTheScreen();
  });

  it('shows the equity curve metrics and closed trades', async () => {
    await renderPortfolio();
    await screen.findByTestId('positions-tab');

    await fireEvent.press(screen.getByRole('tab', { name: 'Performance' }));

    expect(await screen.findByLabelText('Return (TWR): +5.12%')).toBeOnTheScreen();
    expect(screen.getByLabelText('vs SPY: +2.10%')).toBeOnTheScreen();
    expect(screen.getByLabelText('Max drawdown: −3.40%')).toBeOnTheScreen();
    expect(screen.getByLabelText('Win rate: 58%')).toBeOnTheScreen();
    expect(screen.getByLabelText('AMD target, +$500.00')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('radio', { name: '3M' }));
    await waitFor(() => expect(api.performance).toHaveBeenLastCalledWith('3M'));
  });
});
