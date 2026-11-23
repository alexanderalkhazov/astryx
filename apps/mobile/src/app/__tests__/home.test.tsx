import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import HomeScreen from '../(app)/(tabs)/index';
import { saveGoal } from '@/features/onboarding/goal';
import { setViewMode } from '@/features/plain/mode';
import { useSession } from '@/state/session';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
  deleteItemAsync: jest.fn(),
}));
jest.mock('@/features/connection/ConnectionBanner', () => ({ ConnectionBanner: () => null }));
jest.mock('@/lib/api', () => ({
  portfolio: {
    summary: jest.fn(),
    performance: jest.fn(),
    attention: jest.fn(),
    positions: jest.fn(),
    sync: jest.fn(),
    strategyMtd: jest.fn(),
  },
  scanner: { list: jest.fn() },
  market: { breadth: jest.fn() },
  broker: { connections: jest.fn() },
  invest: { get: jest.fn() },
  watchlists: { list: jest.fn() },
}));
const api = jest.requireMock('@/lib/api') as {
  portfolio: Record<'summary' | 'performance' | 'attention' | 'positions' | 'sync' | 'strategyMtd', jest.Mock>;
  scanner: { list: jest.Mock };
  market: { breadth: jest.Mock };
  broker: { connections: jest.Mock };
  invest: { get: jest.Mock };
  watchlists: { list: jest.Mock };
};
const ok = <T,>(data: T) => ({ data });

const SUMMARY = {
  id: 'pf',
  mode: 'paper',
  currency: 'USD',
  equity: '118172.18',
  cash: '7665.03',
  cashAvailable: '7665.03',
  invested: '110507.15',
  unrealizedPnl: '8460.13',
  dayPnl: '484.13',
  dayPnlPct: '0.0041',
  allTimePnl: '10672.93',
  allTimeReturnPct: '0.0993',
  realizedPnl: '0',
  openPositions: 1,
  halted: false,
  asOf: '2026-11-09T19:36:10Z',
  stale: false,
};

async function renderHome() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      index: HomeScreen,
      'proposal/[id]': () => <Text>Proposal screen</Text>,
      'stock/[ticker]': () => <Text>Stock screen</Text>,
      'position/[ticker]': () => <Text>Position screen</Text>,
      discover: () => <Text>Discover screen</Text>,
      portfolio: () => <Text>Portfolio screen</Text>,
      invest: () => <Text>Invest screen</Text>,
      learn: () => <Text>Learn screen</Text>,
    },
    { initialUrl: '/' },
  );
}

beforeEach(async () => {
  jest.clearAllMocks();
  useSession.setState({ user: { id: 'home-user' } as never });
  await setViewMode('home-user', 'pro'); // design 01 is the Pro view; Simple mode is T-191 below
  api.invest.get.mockResolvedValue(ok(null));
  api.watchlists.list.mockResolvedValue(ok({ items: [{ id: 'w', isDefault: true, items: [] }] }));
  api.portfolio.summary.mockResolvedValue(ok(SUMMARY));
  api.portfolio.performance.mockResolvedValue(
    ok({
      range: '1M',
      benchmark: 'SPY',
      series: [
        { t: '2026-10-10T20:00:00Z', equity: '112410.22', benchmark: '112410.22' },
        { t: '2026-11-09T19:36:10Z', equity: '118172.18', benchmark: '114770.83' },
      ],
      metrics: { twrPct: '0.05', benchmarkPct: '0.02', maxDrawdownPct: '-0.03', winRate: null, avgWin: null, avgLoss: null, trades: 0 },
    }),
  );
  api.portfolio.attention.mockResolvedValue(
    ok({
      items: [
        {
          kind: 'pending_proposal',
          title: 'NVDA proposal waiting',
          body: 'Review it before it expires.',
          deepLink: '/proposal/p1',
          severity: 'info',
        },
      ],
    }),
  );
  api.portfolio.positions.mockResolvedValue(
    ok({ items: [{ id: 'x', symbol: 'NVDA', marketValue: '14166.40', unrealizedPnl: '-9.12' }], nextCursor: null }),
  );
  api.portfolio.strategyMtd.mockResolvedValue(
    ok({
      period: 'mtd',
      items: [{ strategyId: 's1', name: 'Base Breakout v3', pnl: '1330.00', pnlPct: '0.0124', trades: 3, illustrative: true }],
    }),
  );
  api.scanner.list.mockResolvedValue(
    ok({
      items: [
        {
          id: 'sig1',
          symbol: 'NVDA',
          type: 'breakout',
          score: '87',
          reason: 'Cleared a 6-week base at $185.90 on 2.4× average volume.',
          levels: { entry: '186.50' },
          metrics: { rewardRisk: '2.11' },
        },
      ],
    }),
  );
  api.broker.connections.mockResolvedValue(ok({ items: [] }));
  api.market.breadth.mockResolvedValue(
    ok({ advancers: 612, decliners: 401, pctAbove50dma: '0.58', sentiment: 'risk_on', asOf: '2026-11-09T19:32:00Z', stale: false }),
  );
});

describe('Home (design 01)', () => {
  it('shows the hero, chart ranges, cash/invested/unrealized and every section', async () => {
    await renderHome();

    expect(await screen.findByLabelText('Total portfolio value · paper account: 118172 dollars and 18 cents')).toBeOnTheScreen();
    expect(screen.getByText('+$484.13 (+0.41%) today')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Cash: $7,665.03')).toBeOnTheScreen();
    expect(screen.getByRole('radio', { name: '1M' })).toBeOnTheScreen();
    expect(await screen.findByText('NVDA proposal waiting')).toBeOnTheScreen();
    expect(await screen.findByLabelText('NVDA, breakout, score 87')).toBeOnTheScreen();
    expect(await screen.findByLabelText('612 advancing, 401 declining, 58% above their 50-day average')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Base Breakout v3, +1.24% month to date, 3 trades')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Alerts, unread' })).toBeOnTheScreen();
  });

  it('names the Alpaca account and when it last synced, or that the connection is lost', async () => {
    const connection = {
      id: 'c1',
      broker: 'alpaca',
      environment: 'paper',
      label: 'Alpaca · paper account',
      keySuffix: 'YSFF',
      accountRef: 'PA3E445ZN',
      status: 'connected',
      permissions: { readPortfolio: true, placeOrders: 'paper_only', depositsWithdrawals: 'never_requested' },
      connectedAt: '2026-11-09T14:00:00Z',
      lastSyncAt: '2026-11-09T19:38:00Z',
      lastError: null,
    };
    api.broker.connections.mockResolvedValue(ok({ items: [connection] }));
    await renderHome();

    expect(await screen.findByLabelText('Total portfolio value · Alpaca paper: 118172 dollars and 18 cents')).toBeOnTheScreen();
    expect(await screen.findByText('Alpaca synced 14:38 ET')).toBeOnTheScreen();

    api.broker.connections.mockResolvedValue(ok({ items: [{ ...connection, status: 'degraded' }] }));
    api.portfolio.sync.mockResolvedValue(ok(SUMMARY));
    await act(async () => screen.getByTestId('home-scroll').props.refreshControl.props.onRefresh());

    expect(await screen.findByText('Alpaca connection lost · approvals paused')).toBeOnTheScreen();
  });

  it('deep-links from Needs attention', async () => {
    await renderHome();

    await fireEvent.press(await screen.findByText('NVDA proposal waiting'));

    await waitFor(() => expect(screen).toHavePathname('/proposal/p1'));
  });

  it('syncs on pull to refresh and says when', async () => {
    api.portfolio.sync.mockResolvedValue(ok({ ...SUMMARY, asOf: '2026-11-09T19:40:00Z' }));
    const { useToasts } = jest.requireActual('@/state/toasts') as typeof import('@/state/toasts');
    await renderHome();
    await screen.findByText('NVDA proposal waiting');

    await act(async () => screen.getByTestId('home-scroll').props.refreshControl.props.onRefresh());

    await waitFor(() => expect(api.portfolio.sync).toHaveBeenCalled());
    await waitFor(() => expect(useToasts.getState().toasts.at(-1)?.title).toBe('Synced 14:40 ET'));
  });
});

describe('Home in Simple mode (T-191)', () => {
  beforeEach(async () => {
    await setViewMode('home-user', 'simple');
    api.portfolio.attention.mockResolvedValue(ok({ items: [] }));
    api.portfolio.positions.mockResolvedValue(ok({ items: [], nextCursor: null }));
  });

  it('leads with one sentence and one next step, with the charts a tap away', async () => {
    await saveGoal('home-user', 'invest');
    await renderHome();

    expect(await screen.findByText('You have $118,172.18 of practice money, up $10,672.93 since you started.')).toBeOnTheScreen();
    expect(await screen.findByTestId('home-next-start_plan')).toHaveTextContent(/Start your investing plan/);
    expect(screen.queryByText('Cash: $7,665.03')).toBeNull();
    expect(screen.queryByText('Market breadth')).toBeNull();

    await fireEvent.press(screen.getByRole('button', { name: 'Show charts and details' }));
    expect(await screen.findByLabelText('Cash: $7,665.03')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Start my plan' }));
    await waitFor(() => expect(screen).toHavePathname('/invest'));
  });

  it('puts a position near its stop first, whatever the goal', async () => {
    await saveGoal('home-user', 'invest');
    api.portfolio.attention.mockResolvedValue(
      ok({
        items: [
          { kind: 'pending_proposal', title: 'AMD proposal waiting', body: '', deepLink: '/proposal/p2', severity: 'info' },
          { kind: 'stop_proximity', title: 'NVDA near its stop', body: '', deepLink: '/position/NVDA', severity: 'warning' },
        ],
      }),
    );
    await renderHome();

    expect(await screen.findByTestId('home-next-stop_proximity')).toHaveTextContent(/NVDA near its stop/);
    await fireEvent.press(screen.getByRole('button', { name: 'Look at it' }));
    await waitFor(() => expect(screen).toHavePathname('/position/NVDA'));
  });

  it('says all good when there is nothing to do', async () => {
    await saveGoal('home-user', 'practice');
    api.portfolio.positions.mockResolvedValue(
      ok({ items: [{ id: 'x', symbol: 'NVDA', marketValue: '14166.40', unrealizedPnl: '-9.12' }], nextCursor: null }),
    );
    await renderHome();

    expect(await screen.findByTestId('home-next-all_good')).toHaveTextContent(/Nothing needs you today\. Check back tomorrow\./);
  });

  it('shows the getting-started checklist from real data, and hides it for good (T-195)', async () => {
    await saveGoal('home-user', 'practice');
    api.watchlists.list.mockResolvedValue(ok({ items: [{ id: 'w', isDefault: true, items: [{ symbol: 'NVDA' }] }] }));
    api.portfolio.positions.mockImplementation(async (status: string) =>
      ok({ items: status === 'closed' ? [{ id: 'c1', symbol: 'AMD' }] : [], nextCursor: null }),
    );
    await renderHome();

    expect(await screen.findByText('Getting started, 2 of 5')).toBeOnTheScreen();
    expect(screen.getByLabelText('Watch a stock, done')).toBeOnTheScreen();
    expect(screen.getByLabelText('Make a practice trade, done')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('link', { name: 'Read one lesson' }));
    await waitFor(() => expect(screen).toHavePathname('/learn'));
  });

  it('can be hidden', async () => {
    await saveGoal('home-user', 'practice');
    await renderHome();

    await fireEvent.press(await screen.findByRole('button', { name: 'Hide getting started' }));
    await waitFor(() => expect(screen.queryByTestId('home-getting-started')).toBeNull());
  });
});
