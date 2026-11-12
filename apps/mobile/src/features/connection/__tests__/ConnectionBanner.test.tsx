import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';

import { ConnectionBanner } from '../ConnectionBanner';

jest.mock('@astryx/api-client', () => ({ ...jest.requireActual('@astryx/api-client'), getSystemStatus: jest.fn() }));
jest.mock('@react-native-community/netinfo', () => ({ useNetInfo: jest.fn(() => ({ isConnected: true })), addEventListener: jest.fn(() => jest.fn()) }));
const mockStatus = (jest.requireMock('@astryx/api-client') as { getSystemStatus: jest.Mock }).getSystemStatus;
const mockNetInfo = (jest.requireMock('@react-native-community/netinfo') as { useNetInfo: jest.Mock }).useNetInfo;

const status = (market: Partial<{ quotesStale: boolean; feedDown: boolean; quotesAsOf: string | null }> = {}) => ({
  database: 'healthy',
  ai: { status: 'healthy', model: null },
  marketData: { provider: 'Fake', quotesAsOf: '2026-11-09T19:12:00Z', quotesStale: false, dailyBarsThrough: '2026-11-06', dailyBarsStale: false, feedDown: false, ...market },
  broker: { adapter: 'Mock', mode: 'paper' },
  features: { liveTrading: false },
});

function renderBanner() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ConnectionBanner />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  mockNetInfo.mockReturnValue({ isConnected: true });
  mockStatus.mockResolvedValue(status());
});

describe('ConnectionBanner', () => {
  it('stays hidden while everything is live', async () => {
    await renderBanner();
    await waitFor(() => expect(mockStatus).toHaveBeenCalled());

    expect(screen.queryByTestId('connection-banner')).not.toBeOnTheScreen();
  });

  it('says the feed is lost, from when values are, and that approvals are disabled (design 22)', async () => {
    mockStatus.mockResolvedValue(status({ feedDown: true }));
    await renderBanner();

    expect(await screen.findByText('Market data connection lost')).toBeOnTheScreen();
    expect(screen.getByText('Values below are from the last sync at 14:12 ET and may be stale. Approvals are disabled until the connection returns.')).toBeOnTheScreen();
  });

  it.each([
    ['the server is unreachable', () => mockStatus.mockRejectedValue(new Error('Network request failed')), "Can't reach Astryx"],
    ['the device is offline', () => mockNetInfo.mockReturnValue({ isConnected: false }), 'No internet connection'],
    ['quotes are past their budget', () => mockStatus.mockResolvedValue(status({ quotesStale: true })), 'Market data is delayed'],
  ])('names the cause when %s', async (_, arrange, title) => {
    arrange();
    await renderBanner();

    expect(await screen.findByText(title)).toBeOnTheScreen();
  });

  it('appears on the next poll after the feed goes down, and Retry clears it once it recovers', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await render(
      <QueryClientProvider client={client}>
        <ConnectionBanner pollMs={50} />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(mockStatus).toHaveBeenCalled());
    expect(screen.queryByTestId('connection-banner')).not.toBeOnTheScreen();

    mockStatus.mockResolvedValue(status({ feedDown: true }));
    expect(await screen.findByText('Market data connection lost')).toBeOnTheScreen(); // no reload: the poll found it

    mockStatus.mockResolvedValue(status());
    await fireEvent.press(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.queryByTestId('connection-banner')).not.toBeOnTheScreen());
    client.clear();
  });
});
