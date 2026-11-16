import { ApiError } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import { useToasts } from '@/state/toasts';

import SettingsScreen from '../(app)/settings/index';
import WatchlistScreen from '../(app)/settings/watchlist';
import StockScreen from '../(app)/stock/[ticker]';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
  deleteItemAsync: jest.fn(),
}));
jest.mock('@/lib/api', () => ({
  watchlists: { list: jest.fn(), star: jest.fn(), unstar: jest.fn(), removeItem: jest.fn(), reorder: jest.fn() },
}));
const mockApi = (jest.requireMock('@/lib/api') as { watchlists: Record<'list' | 'star' | 'unstar' | 'removeItem' | 'reorder', jest.Mock> })
  .watchlists;

const item = (symbol: string, name: string, last: string, changePct: string) => ({
  symbol,
  name,
  sector: null,
  last,
  changePct,
  hasActiveSignal: false,
  held: false,
  note: null,
  addedAt: '2026-11-02T15:00:00Z',
});
const list = (symbols: ReturnType<typeof item>[], version = 3) => ({
  id: 'list-1',
  name: 'Watchlist',
  isDefault: true,
  sortOrder: 0,
  version,
  items: symbols,
});

const NVDA = item('NVDA', 'NVIDIA Corp.', '186.40', '0.0342');
const AMD = item('AMD', 'Advanced Micro Devices', '168.25', '0.0218');
const COST = item('COST', 'Costco Wholesale', '912.30', '-0.0041');

/** A promise the test settles by hand, to look at the optimistic state while a request is in flight. */
function deferred<T = void>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

let queryClient: QueryClient;

function renderAt(initialUrl: string) {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'stock/[ticker]': StockScreen,
      'settings/index': SettingsScreen,
      'settings/watchlist': WatchlistScreen,
      search: () => <Text>Search screen</Text>,
    },
    { initialUrl },
  );
}

const star = () => screen.getByTestId('watchlist-star');

beforeEach(() => {
  jest.clearAllMocks();
  useToasts.getState().clear();
  mockApi.list.mockResolvedValue({ data: { items: [list([AMD])] } });
});

describe('Watchlist star', () => {
  it('fills at once, sends W7, and stays on after the refetch', async () => {
    const request = deferred<{ data: undefined }>();
    mockApi.star.mockReturnValue(request.promise);
    await renderAt('/stock/NVDA');
    await waitFor(() => expect(star()).toBeEnabled());
    expect(star()).not.toBeChecked();

    mockApi.list.mockResolvedValue({ data: { items: [list([AMD, NVDA], 4)] } });
    await fireEvent.press(star());

    await waitFor(() => expect(star()).toBeChecked()); // while the request is still open
    expect(mockApi.star).toHaveBeenCalledWith('NVDA');
    await act(async () => request.resolve({ data: undefined }));
    await waitFor(() => expect(mockApi.list).toHaveBeenCalledTimes(2));
    expect(star()).toBeChecked();
  });

  it('rolls back with a toast when the server refuses', async () => {
    const request = deferred<{ data: undefined }>();
    mockApi.star.mockReturnValue(request.promise);
    await renderAt('/stock/NVDA');
    await waitFor(() => expect(star()).toBeEnabled());

    await fireEvent.press(star());
    await waitFor(() => expect(star()).toBeChecked());
    await act(async () => request.reject(new ApiError({ status: 503, code: 'service_unavailable', title: 'Unavailable' })));

    await waitFor(() => expect(star()).not.toBeChecked());
    expect(useToasts.getState().toasts.map((t) => t.title)).toEqual(["Couldn't add to watchlist"]);
  });

  it('unstars a symbol that is on the list', async () => {
    const request = deferred<{ data: undefined }>();
    mockApi.unstar.mockReturnValue(request.promise);
    await renderAt('/stock/AMD');
    await waitFor(() => expect(star()).toBeChecked());

    mockApi.list.mockResolvedValue({ data: { items: [list([], 4)] } });
    await fireEvent.press(star());

    await waitFor(() => expect(star()).not.toBeChecked());
    expect(mockApi.unstar).toHaveBeenCalledWith('AMD');
    await act(async () => request.resolve({ data: undefined }));
    await waitFor(() => expect(mockApi.list).toHaveBeenCalledTimes(2));
    expect(star()).not.toBeChecked();
  });
});

describe('Settings › Watchlist', () => {
  it('shows the count on the Settings row and opens Manage', async () => {
    mockApi.list.mockResolvedValue({ data: { items: [list([NVDA, AMD, COST])] } });
    await renderAt('/settings');

    await fireEvent.press(await screen.findByLabelText('Watchlist, 3 stocks, Manage'));

    await waitFor(() => expect(screen).toHavePathname('/settings/watchlist'));
  });

  it('lists the stocks with prices and signed change', async () => {
    mockApi.list.mockResolvedValue({ data: { items: [list([NVDA, COST])] } });
    await renderAt('/settings/watchlist');

    expect(await screen.findByText('2 stocks')).toBeOnTheScreen();
    expect(screen.getByLabelText('NVDA, NVIDIA Corp., 186.40, up 3.42%')).toBeOnTheScreen();
    expect(screen.getByText('−0.41%')).toBeOnTheScreen();
  });

  it('reorders with the list ETag version and removes optimistically', async () => {
    mockApi.list.mockResolvedValue({ data: { items: [list([NVDA, AMD, COST])] } });
    mockApi.reorder.mockResolvedValue({ data: list([AMD, NVDA, COST], 4) });
    const removal = deferred<{ data: undefined }>();
    mockApi.removeItem.mockReturnValue(removal.promise);
    await renderAt('/settings/watchlist');
    await fireEvent.press(await screen.findByRole('button', { name: 'Edit' }));

    expect(screen.getByLabelText('Move NVDA up')).toBeDisabled();
    await fireEvent.press(screen.getByLabelText('Move NVDA down'));

    await waitFor(() =>
      expect(mockApi.reorder).toHaveBeenCalledWith(expect.objectContaining({ id: 'list-1', version: 3 }), ['AMD', 'NVDA', 'COST']),
    );

    mockApi.list.mockResolvedValue({ data: { items: [list([AMD, NVDA], 5)] } });
    await fireEvent.press(screen.getByLabelText('Remove COST'));
    await waitFor(() => expect(screen.queryByTestId('watchlist-row-COST')).not.toBeOnTheScreen()); // while the request is still open
    expect(mockApi.removeItem).toHaveBeenCalledWith('list-1', 'COST');
    await act(async () => removal.resolve({ data: undefined }));
    expect(await screen.findByText('2 stocks')).toBeOnTheScreen();
  });

  it('puts a removed stock back and explains when the list changed elsewhere', async () => {
    mockApi.list.mockResolvedValue({ data: { items: [list([NVDA, AMD])] } });
    mockApi.removeItem.mockRejectedValue(new ApiError({ status: 409, code: 'conflict', title: 'Conflict' }));
    await renderAt('/settings/watchlist');
    await fireEvent.press(await screen.findByRole('button', { name: 'Edit' }));

    await fireEvent.press(screen.getByLabelText('Remove AMD'));

    expect(await screen.findByTestId('watchlist-row-AMD')).toBeOnTheScreen();
    expect(useToasts.getState().toasts[0]).toMatchObject({
      title: "Couldn't remove from watchlist",
      body: 'Your watchlist changed elsewhere. It has been reloaded.',
    });
  });

  it('leaves edit mode when the last stock is removed', async () => {
    mockApi.list.mockResolvedValue({ data: { items: [list([AMD])] } });
    mockApi.removeItem.mockResolvedValue({ data: undefined });
    await renderAt('/settings/watchlist');
    await fireEvent.press(await screen.findByRole('button', { name: 'Edit' }));

    mockApi.list.mockResolvedValue({ data: { items: [list([], 4)] } });
    await fireEvent.press(screen.getByLabelText('Remove AMD'));
    expect(await screen.findByText('No stocks yet')).toBeOnTheScreen();

    mockApi.list.mockResolvedValue({ data: { items: [list([NVDA], 5)] } });
    await act(() => queryClient.invalidateQueries()); // the star on a stock screen added one
    expect(await screen.findByRole('button', { name: 'Edit' })).toBeOnTheScreen();
    expect(screen.queryByLabelText('Remove NVDA')).not.toBeOnTheScreen();
  });

  it('offers search when the list is empty', async () => {
    mockApi.list.mockResolvedValue({ data: { items: [list([])] } });
    await renderAt('/settings/watchlist');

    await fireEvent.press(await screen.findByRole('button', { name: 'Search stocks' }));

    await waitFor(() => expect(screen).toHavePathname('/search'));
  });
});
