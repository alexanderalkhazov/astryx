import { ApiError } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import SearchScreen from '../(app)/search';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({ search: { search: jest.fn(), recent: jest.fn(), addRecent: jest.fn() } }));
const mockSearch = (jest.requireMock('@/lib/api') as { search: Record<'search' | 'recent' | 'addRecent', jest.Mock> }).search;

const item = (symbol: string, name: string, last: string, changePct: string, extra: Partial<{ held: boolean; signalType: string }> = {}) => ({
  symbol, name, sector: null, last, changePct, held: false, signalType: null, ...extra,
});

function renderSearch() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'search': SearchScreen,
      'stock/[ticker]': () => <Text>Stock screen</Text>,
    },
    { initialUrl: '/search' },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  mockSearch.recent.mockResolvedValue({ data: { items: [item('NVDA', 'NVIDIA Corp.', '186.40', '0.0342'), item('AMD', 'Advanced Micro Devices', '168.25', '0.0218')] } });
  mockSearch.addRecent.mockResolvedValue({ data: undefined });
});

describe('Search', () => {
  it('shows recents as chips that open the stock', async () => {
    await renderSearch();

    await fireEvent.press(await screen.findByRole('button', { name: 'NVDA' }));

    await waitFor(() => expect(screen).toHavePathname('/stock/NVDA'));
    expect(mockSearch.addRecent).toHaveBeenCalledWith('NVDA');
  });

  it('debounces typing, lists results with tags and signed change, and records the pick', async () => {
    mockSearch.search.mockResolvedValue({
      data: { items: [item('AMZN', 'Amazon.com Inc.', '191.35', '0.0122', { held: true }), item('AMD', 'Advanced Micro Devices', '168.25', '0.0218', { signalType: 'momentum' })] },
    });
    await renderSearch();

    await fireEvent.changeText(screen.getByTestId('search-input'), 'a');
    await fireEvent.changeText(screen.getByTestId('search-input'), 'am');

    expect(await screen.findByText('2 results')).toBeOnTheScreen();
    expect(mockSearch.search).toHaveBeenCalledTimes(1); // "a" never went out
    expect(mockSearch.search).toHaveBeenCalledWith('am', 20, expect.anything());
    expect(screen.getByText('Held')).toBeOnTheScreen();
    expect(screen.getByText('Momentum')).toBeOnTheScreen();
    expect(screen.getByText('+1.22%')).toBeOnTheScreen();
    expect(screen.getByLabelText('AMD, Advanced Micro Devices, Momentum signal, 168.25, up 2.18%')).toBeOnTheScreen();

    await fireEvent.press(screen.getByTestId('search-result-AMZN'));
    await waitFor(() => expect(screen).toHavePathname('/stock/AMZN'));
    expect(mockSearch.addRecent).toHaveBeenCalledWith('AMZN');
  });

  it('says when nothing matches', async () => {
    mockSearch.search.mockResolvedValue({ data: { items: [] } });
    await renderSearch();

    await fireEvent.changeText(screen.getByTestId('search-input'), 'xyz');

    expect(await screen.findByText("No matches for 'xyz'")).toBeOnTheScreen();
  });

  it('shows an inline error with Retry', async () => {
    mockSearch.search.mockRejectedValueOnce(new ApiError({ status: 0, code: 'network_error' }));
    mockSearch.search.mockResolvedValueOnce({ data: { items: [item('NVDA', 'NVIDIA Corp.', '186.40', '0.0342')] } });
    await renderSearch();

    await fireEvent.changeText(screen.getByTestId('search-input'), 'nv');
    await fireEvent.press(await screen.findByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('1 result')).toBeOnTheScreen();
  });

  it('opens the stock even if recording the recent fails', async () => {
    mockSearch.addRecent.mockRejectedValue(new ApiError({ status: 0, code: 'network_error' }));
    await renderSearch();

    await fireEvent.press(await screen.findByRole('button', { name: 'AMD' }));

    await waitFor(() => expect(screen).toHavePathname('/stock/AMD'));
  });
});
