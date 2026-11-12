import { ApiError } from '@astryx/api-client';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import { useDiscoverFilters } from '@/features/discover/filters';
import { useSession } from '@/state/session';

import DiscoverScreen from '../(app)/(tabs)/discover';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({ scanner: { status: jest.fn(), list: jest.fn(), count: jest.fn() } }));
const api = (jest.requireMock('@/lib/api') as { scanner: Record<'status' | 'list' | 'count', jest.Mock> }).scanner;

const NVDA = {
  id: 's1', rank: 1, symbol: 'NVDA', name: 'NVIDIA Corp.', sector: 'Semiconductors', type: 'breakout', horizon: 'swing', price: '186.40', changePct: '0.0342', score: '87',
  metrics: { relVolume: '2.40', relStrength: '94', rewardRisk: '2.11', rsi14: '64.2' },
  reason: 'Cleared a 6-week base at $185.90 on 2.4× average volume.',
  levels: { orderType: 'buy_stop', entry: '186.50', stop: '178.20', target: '204.00' },
  strategy: { id: 'st', name: 'Base Breakout v3', version: 3 },
  evaluatedAt: '2026-11-09T19:32:00Z', dataAsOf: '2026-11-09T19:31:00Z', status: 'active', scoreBreakdown: null, proposalId: null,
  sparkline: ['180.24', '186.40'], volume: 412800000, expiresAt: '2026-11-10T19:32:00Z',
};
const counts = (all: number) => ({ all, momentum: 0, breakout: all, pullback: 0, unusualVolume: 0, earnings: 0 });
const page = (items: unknown[], all = items.length) => ({ data: { items, nextCursor: null, counts: counts(all), scanner: { lastRunAt: '2026-11-09T19:32:00Z', dataAsOf: null } } });
const STATUS = { status: 'live', symbolsTotal: 1031, lastRunAt: '2026-11-09T19:32:00Z', dataAsOf: '2026-11-09T19:31:00Z', errorCode: null };

function renderDiscover() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      discover: DiscoverScreen,
      'stock/[ticker]': () => <Text>Stock screen</Text>,
      search: () => <Text>Search screen</Text>,
    },
    { initialUrl: '/discover' },
  );
}

beforeEach(async () => {
  jest.clearAllMocks();
  await AsyncStorage.clear();
  useDiscoverFilters.setState({ filters: { sort: 'score' }, userId: null });
  useSession.setState({ user: { id: 'user-1' } as never });
  api.status.mockResolvedValue({ data: STATUS });
  api.list.mockResolvedValue(page([NVDA]));
  api.count.mockResolvedValue({ data: { count: 1 } });
});

describe('Discover', () => {
  it('shows the scanner line, chip counts and ranked rows (design 02)', async () => {
    await renderDiscover();

    expect(await screen.findByTestId('opportunity-NVDA')).toBeOnTheScreen();
    expect(screen.getByText('Scanner live · 1,031 symbols · Updated 14:32 ET')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'All, 1' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Breakouts, 1' })).toBeOnTheScreen();
    expect(screen.getByText('01')).toBeOnTheScreen();
    expect(screen.getByText('Cleared a 6-week base at $185.90 on 2.4× average volume.')).toBeOnTheScreen();
    expect(screen.getByText('2.4×')).toBeOnTheScreen();
    expect(screen.getByText('412.8M')).toBeOnTheScreen();
    expect(screen.getByText('2.1')).toBeOnTheScreen(); // R:R
    expect(screen.getByText('64')).toBeOnTheScreen(); // RSI
    expect(screen.getByText('1 setup · ranked by opportunity score')).toBeOnTheScreen();
  });

  it('opens the stock from a row', async () => {
    await renderDiscover();

    await fireEvent.press(await screen.findByTestId('opportunity-NVDA'));

    await waitFor(() => expect(screen).toHavePathname('/stock/NVDA'));
  });

  it('filters by type from the chip row', async () => {
    await renderDiscover();
    await screen.findByTestId('opportunity-NVDA');

    await fireEvent.press(screen.getByRole('button', { name: 'Momentum, 0' }));

    await waitFor(() => expect(api.list).toHaveBeenLastCalledWith(expect.objectContaining({ type: 'momentum' }), expect.anything()));
  });

  it('hides signals when the scanner is unavailable and retries (design 23)', async () => {
    api.status.mockResolvedValue({ data: { ...STATUS, status: 'unavailable', errorCode: 'SCN-504' } });
    api.list.mockRejectedValue(new ApiError({ status: 503, code: 'scanner_unavailable', title: 'Unavailable' }));
    await renderDiscover();

    expect(await screen.findByText('Scanner feed unavailable')).toBeOnTheScreen();
    expect(screen.queryByTestId('opportunity-NVDA')).not.toBeOnTheScreen();
    expect(await screen.findByText('SCN-504 · auto-retry in 60 s')).toBeOnTheScreen();
    const before = api.list.mock.calls.length;
    await fireEvent.press(screen.getByRole('button', { name: 'Retry now' }));
    await waitFor(() => expect(api.list.mock.calls.length).toBeGreaterThan(before));
  });

  it('applies sheet filters with a live count, shows them as removable chips and persists them per user', async () => {
    await renderDiscover();
    await screen.findByTestId('opportunity-NVDA');

    await fireEvent.press(screen.getByTestId('discover-filters'));
    await fireEvent.press(await screen.findByRole('button', { name: 'Mega · >$200B' }));
    await waitFor(() => expect(api.count).toHaveBeenLastCalledWith(expect.objectContaining({ marketCap: 'mega' })));
    expect(await screen.findByText('Show 1 setup')).toBeOnTheScreen();
    await fireEvent.press(screen.getByTestId('filters-apply'));

    await waitFor(() => expect(api.list).toHaveBeenLastCalledWith(expect.objectContaining({ marketCap: 'mega' }), expect.anything()));
    expect(screen.getByRole('button', { name: 'Remove filter Mega' })).toBeOnTheScreen();
    await waitFor(async () => expect(JSON.parse((await AsyncStorage.getItem('discover-filters:user-1'))!)).toMatchObject({ marketCap: 'mega' }));

    await fireEvent.press(screen.getByRole('button', { name: 'Clear all' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove filter Mega' })).not.toBeOnTheScreen());
  });

  it('restores saved filters for the signed-in user', async () => {
    await AsyncStorage.setItem('discover-filters:user-1', JSON.stringify({ sort: 'rr', minScore: 80 }));

    await renderDiscover();

    await waitFor(() => expect(api.list).toHaveBeenLastCalledWith(expect.objectContaining({ sort: 'rr', minScore: 80 }), expect.anything()));
    expect(await screen.findByRole('button', { name: 'Remove filter Score 80+' })).toBeOnTheScreen();
  });

  it('offers Reset and Adjust when nothing matches (design 21)', async () => {
    useDiscoverFilters.setState({ filters: { sort: 'score', sector: 'Information Technology' }, userId: 'user-1' });
    api.list.mockResolvedValue(page([], 0));
    await renderDiscover();

    expect(await screen.findByText('No setups match these filters')).toBeOnTheScreen();
    expect(screen.getByText("Nothing in today's scan fits Technology. The scanner re-runs every 15 minutes.")).toBeOnTheScreen();
    api.list.mockResolvedValue(page([NVDA]));
    await fireEvent.press(screen.getByRole('button', { name: 'Reset filters' }));

    expect(await screen.findByTestId('opportunity-NVDA')).toBeOnTheScreen();
  });

  it('shows five staggered skeleton rows while loading (design 20)', async () => {
    api.list.mockReturnValue(new Promise(() => undefined));
    await renderDiscover();

    expect(await screen.findByTestId('discover-loading')).toBeOnTheScreen();
    expect(screen.getAllByLabelText('Loading')).toHaveLength(5);
  });
});
