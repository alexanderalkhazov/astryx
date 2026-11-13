import type { ActivityItem } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import ActivityScreen from '../(app)/(tabs)/activity';

jest.mock('expo-crypto', () => ({ randomUUID: () => 'cancel-key-0001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@astryx/api-client', () => ({
  ...jest.requireActual('@astryx/api-client'),
  getSystemStatus: jest.fn(async () => ({ database: 'ok', ai: { status: 'ok', model: 'qwen3.5:0.8b' }, marketData: { provider: 'Fake', quotesStale: false, feedDown: false }, broker: { adapter: 'mock', mode: 'paper' }, features: { liveTrading: false } })),
}));
jest.mock('@/lib/api', () => ({
  notifications: { list: jest.fn(), markRead: jest.fn(async () => ({})), markAllRead: jest.fn(async () => ({})) },
  paper: { working: jest.fn(), cancel: jest.fn() },
  scanner: { status: jest.fn(async () => ({ data: { status: 'live' } })) },
}));
const api = jest.requireMock('@/lib/api') as { notifications: Record<'list' | 'markRead' | 'markAllRead', jest.Mock>; paper: Record<'working' | 'cancel', jest.Mock> };
const ok = <T,>(data: T) => ({ data });

const item = (over: Partial<ActivityItem>): ActivityItem => ({
  id: 'n1', category: 'orders', type: 'order_filled', title: 'Filled (simulated) · NVDA', body: 'Bought 76 NVDA at $186.52 in your paper account.', severity: 'success',
  deepLink: '/position/NVDA', entityType: 'order', entityId: 'o1', createdAt: new Date().toISOString(), read: false, ...over,
});

const WORKING = { id: 'o2', brokerOrderId: 'SIM-AAAAAA', mode: 'paper', broker: 'mock', status: 'working', purpose: 'entry', symbol: 'AMD', side: 'buy', orderType: 'buy_stop', quantity: '50', stopPrice: '160.00', limitPrice: null, fill: null, attached: null, positionId: null, proposalId: 'p2', submittedAt: new Date().toISOString(), expiresAt: null };

async function renderActivity(url = '/activity') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      activity: ActivityScreen,
      'position/[ticker]': () => <Text>Position screen</Text>,
    },
    { initialUrl: url },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  api.notifications.list.mockResolvedValue(ok({ items: [item({}), item({ id: 'n2', category: 'alerts', type: 'stop_proximity', title: 'TSLA near its stop', body: 'TSLA is 1.2% above its stop at $236.00.', severity: 'warning', deepLink: '/position/TSLA', read: true })], nextCursor: null, unreadCount: 1 }));
  api.paper.working.mockResolvedValue(ok({ items: [WORKING, { ...WORKING, id: 'exit', purpose: 'exit_stop' }] }));
});

describe('Activity (design 17)', () => {
  it('shows pending orders, the dated timeline and system status', async () => {
    await renderActivity();

    expect(await screen.findByTestId('pending-AMD')).toBeOnTheScreen();
    expect(screen.getByText('50 sh · triggers at 160.00 · SIM-AAAAAA')).toBeOnTheScreen();
    expect(await screen.findByText(/^Today · /)).toBeOnTheScreen();
    expect(screen.getByLabelText('Filled (simulated) · NVDA. Bought 76 NVDA at $186.52 in your paper account., unread')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Data feed: Live')).toBeOnTheScreen();
    expect(screen.getByLabelText('Broker: Paper · mock')).toBeOnTheScreen();
  });

  it('opens an event, marking it read', async () => {
    await renderActivity();

    await fireEvent.press(await screen.findByLabelText(/^Filled \(simulated\) · NVDA/));

    await waitFor(() => expect(screen).toHavePathname('/position/NVDA'));
    expect(api.notifications.markRead).toHaveBeenCalledWith('n1');
  });

  it('filters by category, starting from the bell filter', async () => {
    await renderActivity('/activity?filter=alerts');

    await waitFor(() => expect(api.notifications.list).toHaveBeenCalledWith('alerts'));
    expect(screen.queryByTestId('pending-AMD')).not.toBeOnTheScreen(); // pending orders belong to All and Orders
    await fireEvent.press(screen.getByRole('button', { name: /^Orders/ }));
    await waitFor(() => expect(api.notifications.list).toHaveBeenLastCalledWith('orders'));
  });

  it('cancels a working order through the dialog', async () => {
    api.paper.cancel.mockResolvedValue(ok({ ...WORKING, status: 'cancelled' }));
    await renderActivity();

    await fireEvent.press(await screen.findByRole('button', { name: 'Cancel AMD order' }));
    expect(screen.getByText('The order stops working and its reserved cash is released. The proposal stays in history.')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Cancel order' }));

    await waitFor(() => expect(api.paper.cancel).toHaveBeenCalledWith('o2', 'cancel-key-0001'));
  });

  it('shows the dashed empty card when nothing is pending', async () => {
    api.paper.working.mockResolvedValue(ok({ items: [] }));
    await renderActivity();

    expect(await screen.findByTestId('pending-empty')).toBeOnTheScreen();
  });
});
