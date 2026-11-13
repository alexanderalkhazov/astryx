import { ApiError, type PositionDetail } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import PositionScreen from '../(app)/position/[ticker]';

jest.mock('expo-crypto', () => ({ randomUUID: () => 'close-key-0001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({ portfolio: { positionBySymbol: jest.fn(), closePreview: jest.fn(), close: jest.fn() }, market: { bars: jest.fn() } }));
const mocks = jest.requireMock('@/lib/api') as { portfolio: Record<'positionBySymbol' | 'closePreview' | 'close', jest.Mock>; market: { bars: jest.Mock } };
const ok = <T,>(data: T) => ({ data });

/** Design 10: 76 NVDA at 186.52, last 186.40. */
const DETAIL = (over: Partial<PositionDetail> = {}): PositionDetail => ({
  id: 'pos1', symbol: 'NVDA', name: 'NVIDIA Corp.', status: 'open', isNew: true, quantity: '76', avgEntryPrice: '186.52', costBasis: '14175.52', last: '186.40', marketValue: '14166.40',
  unrealizedPnl: '-9.12', unrealizedPct: '-0.0006', track: { stop: '178.20', entry: '186.52', current: '186.40', target: '204.00', progressPct: '0.3209' }, feesPaid: '0.00',
  openedAt: '2026-11-09T19:36:08Z', entryOrderId: 'SIM-4G3AC2', generatedBy: { source: 'signal', strategyId: 'st1', strategyName: 'Base Breakout v3', at: '2026-11-09T19:32:00Z' },
  exitPrice: null, exitReason: null, realizedPnl: null, closedAt: null, quoteAsOf: '2026-11-09T19:36:10Z', ...over,
});

async function renderPosition() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'position/[ticker]': PositionScreen,
      'strategy/[id]': () => <Text>Strategy screen</Text>,
      'stock/[ticker]': () => <Text>Stock screen</Text>,
      portfolio: () => <Text>Portfolio screen</Text>,
    },
    { initialUrl: '/position/NVDA' },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  mocks.portfolio.positionBySymbol.mockResolvedValue(ok(DETAIL()));
  mocks.market.bars.mockResolvedValue(ok({ bars: [{ t: '2026-10-10T20:00:00Z', c: '175.00' }, { t: '2026-11-09T20:00:00Z', c: '186.40' }] }));
});

describe('Position detail (designs 10 and 24)', () => {
  it('shows value, the NEW · SIMULATED badge, the level track, facts and the strategy that generated it', async () => {
    await renderPosition();

    expect(await screen.findByText('$14,166.40')).toBeOnTheScreen();
    expect(screen.getByText('NEW · SIMULATED')).toBeOnTheScreen();
    expect(screen.getByText('−$9.12 (−0.06%) unrealized')).toBeOnTheScreen();
    expect(screen.getByLabelText('Stop 178.20, entry 186.52, current 186.40, target 204.00')).toBeOnTheScreen();
    expect(screen.getByLabelText('Opened: Nov 9 · SIM-4G3AC2')).toBeOnTheScreen();
    expect(mocks.portfolio.positionBySymbol).toHaveBeenCalledWith('NVDA');
    await fireEvent.press(screen.getByTestId('generated-by'));
    await waitFor(() => expect(screen).toHavePathname('/strategy/st1'));
  });

  it('previews the close on the server, keeps it open by default and closes with one key', async () => {
    mocks.portfolio.closePreview.mockResolvedValue(ok({ quantity: '76', marketValue: '14166.40', estExitPrice: '186.38', estFees: '0.00', estRealizedPnl: '-10.64', asOf: '2026-11-09T19:36:10Z' }));
    mocks.portfolio.close.mockResolvedValue(ok({ position: { status: 'closed', realizedPnl: '-10.64' } }));
    await renderPosition();

    await fireEvent.press(await screen.findByTestId('close-position'));
    expect(await screen.findByText('Sell 76 shares at about 186.38 (market value $14,166.40). Estimated realized P&L −$10.64.')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Keep open' }));
    expect(mocks.portfolio.close).not.toHaveBeenCalled();

    await fireEvent.press(screen.getByTestId('close-position'));
    await screen.findByText(/Estimated realized P&L/);
    await fireEvent.press(screen.getAllByRole('button', { name: 'Close position' }).at(-1)!);

    await waitFor(() => expect(mocks.portfolio.close).toHaveBeenCalledWith('pos1', 'close-key-0001'));
  });

  it('shows the closed state pointing to Performance', async () => {
    mocks.portfolio.positionBySymbol.mockResolvedValue(ok(DETAIL({ status: 'closed', exitPrice: '204.00', exitReason: 'target', realizedPnl: '1330.00', closedAt: '2026-11-12T15:00:00Z', last: null, marketValue: null, unrealizedPnl: null })));
    await renderPosition();

    expect(await screen.findByText('POSITION CLOSED')).toBeOnTheScreen();
    expect(screen.getByText(/Realized P&L is in Portfolio › Performance\./)).toBeOnTheScreen();
    expect(screen.queryByTestId('close-position')).not.toBeOnTheScreen();
  });

  it('says when there is no position for the symbol', async () => {
    mocks.portfolio.positionBySymbol.mockRejectedValue(new ApiError({ status: 404, code: 'not_found' }));
    await renderPosition();

    expect(await screen.findByText('No NVDA position')).toBeOnTheScreen();
  });
});
