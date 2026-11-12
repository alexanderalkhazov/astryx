import { ApiError } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import StockScreen from '../(app)/stock/[ticker]';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({
  market: { instrument: jest.fn(), quote: jest.fn(), bars: jest.fn(), fundamentals: jest.fn(), news: jest.fn() },
  watchlists: { list: jest.fn(), star: jest.fn(), unstar: jest.fn() },
}));
const api = jest.requireMock('@/lib/api') as {
  market: Record<'instrument' | 'quote' | 'bars' | 'fundamentals' | 'news', jest.Mock>;
  watchlists: Record<'list' | 'star' | 'unstar', jest.Mock>;
};

const NVDA = { symbol: 'NVDA', exchange: 'XNAS', name: 'NVIDIA Corp.', assetType: 'stock', currency: 'USD', sector: 'Information Technology', industry: 'Semiconductors', marketCap: '4550000000000', isActive: true };
const quote = (extra: Partial<{ stale: boolean }> = {}) => ({
  symbol: 'NVDA', last: '186.40', change: '6.16', changePct: '0.0342', open: '180.60', high: '186.77', low: '180.05', prevClose: '180.24',
  bid: null, ask: null, volume: 412883000, asOf: '2026-11-09T19:32:00Z', quality: 'ok', stale: false, source: 'fake', ...extra,
});

/** Daily bars from Jun 1: 160 sessions, so a 3M view has warm-up bars before its cut-off. */
const dailyBars = Array.from({ length: 160 }, (_, i) => {
  const date = new Date(Date.UTC(2026, 5, 1 + i));
  const d = date.toISOString().slice(0, 10);
  return { t: `${d}T13:30:00Z`, d, o: '180.60', h: '186.77', l: '180.05', c: '186.40', v: 412883000 };
});
const barsResponse = (bars = dailyBars) => ({ data: { symbol: 'NVDA', interval: '1d', adjusted: false, asOf: null, stale: false, bars, levels: null } });

function renderStock(symbol = 'NVDA') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'stock/[ticker]': StockScreen,
      search: () => <Text>Search screen</Text>,
    },
    { initialUrl: `/stock/${symbol}` },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  api.market.instrument.mockResolvedValue({ data: NVDA });
  api.market.quote.mockResolvedValue({ data: quote() });
  api.market.bars.mockResolvedValue(barsResponse());
  api.market.fundamentals.mockResolvedValue({
    data: { symbol: 'NVDA', asOf: null, beta: '1.72', peRatio: '48.60', epsTtm: null, nextEarningsDate: '2026-11-25', marketCap: '4550000000000', sector: 'Information Technology', industry: 'Semiconductors' },
  });
  api.market.news.mockResolvedValue({ data: { items: [{ id: 'n1', headline: 'NVIDIA shares climb', source: 'Sample Wire', url: 'https://example.com/n1', publishedAt: '2026-11-12T12:05:00Z', summary: null }] } });
  api.watchlists.list.mockResolvedValue({ data: { items: [{ id: 'list-1', name: 'Watchlist', isDefault: true, sortOrder: 0, version: 1, items: [] }] } });
  api.watchlists.star.mockResolvedValue({ data: undefined });
});

describe('Stock screen', () => {
  it('shows price, signed change, industry, the illustrative tag and the last bar', async () => {
    await renderStock();

    expect(await screen.findByTestId('stock-price')).toHaveTextContent('186.40');
    expect(screen.getByText('+6.16 (+3.42%) today')).toBeOnTheScreen();
    expect(screen.getByText('Semiconductors')).toBeOnTheScreen();
    expect(screen.getByText('14:32 ET · illustrative')).toBeOnTheScreen();
    expect(await screen.findByLabelText('Nov 7: open 180.60, high 186.77, low 180.05, close 186.40, volume 412.8M')).toBeOnTheScreen();
    expect(screen.getByText('NVIDIA Corp.')).toBeOnTheScreen();
  });

  it('fetches daily history with warm-up bars so the 3M chart starts at the cut-off', async () => {
    await renderStock();

    // The last bar is Nov 7, so 3M starts Aug 7: earlier bars only feed the moving averages.
    expect(await screen.findByLabelText(/^Price chart, 93 bars from Aug 7 to Nov 7\./)).toBeOnTheScreen();
    expect(api.market.bars).toHaveBeenCalledWith('NVDA', expect.objectContaining({ interval: '1d', from: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) }));
  });

  it('switches timeframe; intraday ranges use the server range', async () => {
    await renderStock();
    await screen.findByTestId('candle-chart');

    await fireEvent.press(screen.getByRole('radio', { name: '1D' }));

    await waitFor(() => expect(api.market.bars).toHaveBeenCalledWith('NVDA', { timeframe: '1D' }));
  });

  it('says when the symbol does not exist', async () => {
    api.market.instrument.mockRejectedValue(new ApiError({ status: 404, code: 'instrument_not_found', title: 'Not found' }));
    await renderStock('ZZZZ');

    expect(await screen.findByText('No stock called ZZZZ')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Search stocks' }));
    await waitFor(() => expect(screen).toHavePathname('/search'));
  });

  it('offers a retry when price history fails, and warns when quotes are stale', async () => {
    api.market.bars.mockRejectedValue(new ApiError({ status: 503, code: 'market_data_unavailable', title: 'Unavailable' }));
    api.market.quote.mockResolvedValue({ data: quote({ stale: true }) });
    await renderStock();

    expect(await screen.findByText('Price history unavailable')).toBeOnTheScreen();
    expect(screen.getByText('Prices may be out of date')).toBeOnTheScreen();
    api.market.bars.mockResolvedValue(barsResponse());
    await fireEvent.press(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByTestId('candle-chart')).toBeOnTheScreen();
  });

  it('shows fundamentals with dashes for missing values, and news', async () => {
    await renderStock();
    await screen.findByTestId('stock-price');

    await fireEvent.press(screen.getByRole('tab', { name: 'Fundamentals' }));
    expect(await screen.findByLabelText('Market cap, $4.55T')).toBeOnTheScreen();
    expect(screen.getByLabelText('EPS (TTM), not provided')).toBeOnTheScreen();
    expect(screen.getByLabelText('P/E (TTM), 48.6')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('tab', { name: 'News' }));
    expect(await screen.findByText('NVIDIA shares climb')).toBeOnTheScreen();
  });

  it('adds to the watchlist from the bottom bar', async () => {
    await renderStock();
    const button = await screen.findByTestId('stock-watchlist-button');
    await waitFor(() => expect(button).toBeEnabled());
    const nvdaItem = { symbol: 'NVDA', name: 'NVIDIA Corp.', sector: null, last: '186.40', changePct: '0.0342', hasActiveSignal: false, held: false, note: null, addedAt: '2026-11-12T15:00:00Z' };
    api.watchlists.list.mockResolvedValue({ data: { items: [{ id: 'list-1', name: 'Watchlist', isDefault: true, sortOrder: 0, version: 2, items: [nvdaItem] }] } });

    await fireEvent.press(button);

    expect(api.watchlists.star).toHaveBeenCalledWith('NVDA');
    expect(await screen.findByText('On watchlist')).toBeOnTheScreen();
  });
});
