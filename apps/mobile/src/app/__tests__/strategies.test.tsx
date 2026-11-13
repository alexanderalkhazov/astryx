import { ApiError, type BacktestRun, type StrategyDetail, type StrategySummary } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import StrategiesScreen from '../(app)/(tabs)/strategies';
import StrategyScreen from '../(app)/strategy/[id]';

jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({ strategies: { list: jest.fn(), compare: jest.fn(), get: jest.fn(), run: jest.fn() } }));
const mocks = jest.requireMock('@/lib/api') as { strategies: Record<'list' | 'compare' | 'get' | 'run', jest.Mock> };
const ok = <T,>(data: T) => ({ data });

const FULL = { cagr: '0.169', sharpe: '1.31', maxDrawdown: '-0.148', winRate: '0.46', trades: 412, profitFactor: '1.86', avgTradePct: '0.0190', expectancyR: '0.42', multiple: '6.02' };
const IS = { cagr: '0.184', sharpe: '1.42', maxDrawdown: '-0.148', winRate: '0.47' };
const OOS = { cagr: '0.141', sharpe: '1.11', maxDrawdown: '-0.123', winRate: '0.44' };

const SUMMARY = (over: Partial<StrategySummary> = {}): StrategySummary => ({
  id: 'st1', code: 'base_breakout', name: 'Base Breakout v3', description: 'Buys breakouts from tight multi-week bases on heavy volume.', status: 'active', displayOrder: 1, version: 3,
  liveSince: '2025-03-03', live: { sinceInceptionPct: '0.114', mtdPct: '0.028', mtdPnl: '1330.00', mtdTrades: 3, illustrative: true },
  metrics: { cagr: '0.169', sharpe: '1.31', maxDrawdown: '-0.148', winRate: '0.46' }, sparkline: ['1.00', '2.00', '6.02'], backtestStart: '2015-01-01', backtestEnd: '2026-06-30', ...over,
});

const PULLBACK = SUMMARY({ id: 'st3', code: 'trend_pullback', name: 'Trend Pullback', status: 'active_guarded', live: { sinceInceptionPct: '0.02', mtdPct: '-0.011', mtdPnl: '-712.40', mtdTrades: 2, illustrative: true } });

const DETAIL = (over: Partial<StrategyDetail> = {}): StrategyDetail => ({
  summary: SUMMARY(),
  rules: ['Price forms a base of 4–10 weeks.', 'Breaks out on 1.8× average volume.'],
  version: {
    id: 'v3', number: 3, state: 'published', codeVersion: 'base_breakout@1',
    parameters: [{ key: 'baseWeeks', label: 'Base length', unit: 'wk', default: 6, min: 4, max: 10, step: 1, value: 6 }],
  },
  latestRun: { id: 'run1', runNumber: 1, status: 'succeeded', periodStart: '2015-01-01', periodEnd: '2026-06-30', oosStart: '2023-01-01', finishedAt: '2026-06-30T22:00:00Z', metrics: { full: FULL, inSample: IS, outOfSample: OOS } },
  todaysSignals: [{ id: 'sig1', symbol: 'NVDA', type: 'breakout', score: '87', entry: '186.50', evaluatedAt: '2026-11-09T19:32:00Z' }],
  ...over,
});

const RUN: BacktestRun = {
  id: 'run1', strategyId: 'st1', runNumber: 1, version: 3, status: 'succeeded', periodStart: '2015-01-01', periodEnd: '2026-06-30', oosStart: '2023-01-01', initialCapital: '100000.00',
  assumptions: { fees: '0.00 per order', slippageBps: 5, universe: 'Current US large caps (illustrative)', survivorship: "Uses today's universe: results are biased upward.", benchmark: { name: 'S&P 500', multiple: '3.72' } },
  metrics: { full: FULL, inSample: IS, outOfSample: OOS },
  equityCurve: [['2015-01-01', '100000.00'], ['2023-01-01', '400000.00'], ['2026-06-01', '602000.00']],
  drawdownCurve: [['2015-01-01', '0.0000'], ['2023-01-01', '-0.1480'], ['2026-06-01', '0.0000']],
  monthlyReturns: [['2015-02', '-0.0420'], ['2015-03', '0.0190']],
  finishedAt: '2026-06-30T22:00:00Z', errorCode: null,
};

async function renderAt(initialUrl: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      strategies: StrategiesScreen,
      'strategy/[id]': StrategyScreen,
      'stock/[ticker]': () => <Text>Stock screen</Text>,
    },
    { initialUrl },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  mocks.strategies.list.mockResolvedValue(ok({ items: [SUMMARY(), PULLBACK] }));
  mocks.strategies.compare.mockResolvedValue(
    ok({
      series: [{ strategyId: 'st1', name: 'Base Breakout v3', points: [{ d: '2015-01-01', growth: '1.0000' }, { d: '2026-06-01', growth: '6.0200' }] }],
      metrics: [{ strategyId: 'st1', name: 'Base Breakout v3', metrics: FULL }],
    }),
  );
  mocks.strategies.get.mockResolvedValue(ok(DETAIL()));
  mocks.strategies.run.mockResolvedValue(ok(RUN));
});

describe('Strategy lab (designs 13 and 14)', () => {
  it('lists strategies with status, headline metrics, live MTD and the illustrative caption', async () => {
    await renderAt('/strategies');

    expect(await screen.findByText('Base Breakout v3')).toBeOnTheScreen();
    expect(screen.getByText('2 strategies · backtests Jan 2015 – Jun 2026 · illustrative results')).toBeOnTheScreen();
    expect(screen.getByText('Active')).toBeOnTheScreen();
    expect(screen.getByText('Active · guard')).toBeOnTheScreen();
    expect(screen.getAllByLabelText('CAGR: 16.9%')).toHaveLength(2);
    expect(screen.getAllByLabelText('Max DD: −14.8%')).toHaveLength(2);
    expect(screen.getByText(' · MTD +2.8%')).toBeOnTheScreen();
    expect(screen.getByText(/historical and illustrative/)).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('strategy-trend_pullback'));
    await waitFor(() => expect(screen).toHavePathname('/strategy/trend_pullback'));
  });

  it('compares growth of $1 and full-period metrics side by side', async () => {
    await renderAt('/strategies');
    await screen.findByText('Base Breakout v3');

    await fireEvent.press(screen.getByRole('radio', { name: 'Compare' }));

    expect(await screen.findByTestId('compare-metrics')).toBeOnTheScreen();
    expect(mocks.strategies.compare).toHaveBeenCalledWith(['st1', 'st3']);
    expect(screen.getByLabelText('Growth of one dollar: Base Breakout v3 6.02 times')).toBeOnTheScreen();
    expect(screen.getByLabelText('Base Breakout v3 Profit factor: 1.86')).toBeOnTheScreen();
    expect(screen.getByLabelText('Base Breakout v3 Growth of $1: 6.02×')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Trend Pullback' }));
    await waitFor(() => expect(mocks.strategies.compare).toHaveBeenLastCalledWith(['st1']));
  });

  it('offers Retry when the catalog fails to load', async () => {
    mocks.strategies.list.mockRejectedValueOnce(new Error('offline'));
    await renderAt('/strategies');

    expect(await screen.findByText("Couldn't load strategies")).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Base Breakout v3')).toBeOnTheScreen();
  });
});

describe('Strategy detail (designs 11 and 12)', () => {
  it('shows rules, illustrative live results and today’s signals on Overview', async () => {
    await renderAt('/strategy/base_breakout');

    expect(await screen.findByText('Price forms a base of 4–10 weeks.')).toBeOnTheScreen();
    expect(mocks.strategies.get).toHaveBeenCalledWith('base_breakout');
    expect(screen.getByLabelText('Month to date: +2.8%')).toBeOnTheScreen();
    expect(screen.getByText(/illustrative until your fills are attributed/)).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'NVDA, entry 186.50, score 87' }));
    await waitFor(() => expect(screen).toHavePathname('/stock/NVDA'));
  });

  it('shows the backtest: OOS band, drawdown, eight metrics, IS vs OOS, heatmap and assumptions', async () => {
    await renderAt('/strategy/base_breakout');
    await screen.findByText('Price forms a base of 4–10 weeks.');

    await fireEvent.press(screen.getByRole('tab', { name: 'Backtest' }));

    expect(await screen.findByTestId('backtest-tab')).toBeOnTheScreen();
    expect(mocks.strategies.run).toHaveBeenCalledWith('st1', 'run1');
    expect(screen.getByText('Run #1 · 2026-06-30 · v3')).toBeOnTheScreen();
    expect(screen.getByText('Shaded: out of sample from Jan 2023')).toBeOnTheScreen();
    expect(screen.getByLabelText('Drawdown, deepest −14.8%')).toBeOnTheScreen();
    for (const label of ['CAGR: 16.9%', 'Sharpe: 1.31', 'Max DD: −14.8%', 'Win rate: 46%', 'Profit factor: 1.86', 'Avg trade: +1.90%', 'Trades: 412', 'Expectancy: 0.42R']) {
      expect(screen.getByLabelText(label)).toBeOnTheScreen();
    }
    expect(screen.getByLabelText('Out of sample · 2023–2026 CAGR: 14.1%')).toBeOnTheScreen();
    expect(screen.getByLabelText('In sample · 2015–2022 Sharpe: 1.42')).toBeOnTheScreen();
    expect(screen.getByLabelText('Feb 2015: −4.2%')).toBeOnTheScreen();
    expect(screen.getByText(/biased upward/)).toBeOnTheScreen();
    expect(screen.getByText('Growth of $1: 6.02× vs S&P 500 3.72×')).toBeOnTheScreen();
    expect(screen.getByText(/historical and illustrative/)).toBeOnTheScreen();
  });

  it('renders Configure read-only with Coming soon and a disabled re-run', async () => {
    await renderAt('/strategy/base_breakout');
    await screen.findByText('Price forms a base of 4–10 weeks.');

    await fireEvent.press(screen.getByRole('tab', { name: 'Configure' }));

    expect(await screen.findByText('COMING SOON')).toBeOnTheScreen();
    expect(screen.getByText('Default 6 wk · range 4–10')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Run backtest · 2015–2026' })).toBeDisabled();
  });

  it('says when the strategy does not exist', async () => {
    mocks.strategies.get.mockRejectedValueOnce(new ApiError({ status: 404, code: 'not_found', detail: 'No strategy with that id.' }));
    await renderAt('/strategy/nope');

    expect(await screen.findByText('Strategy not found')).toBeOnTheScreen();
  });
});
