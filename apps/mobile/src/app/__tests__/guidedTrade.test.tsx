import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Alert, Text } from 'react-native';

import GuidedTradeScreen from '../(app)/guided-trade';
import { useSession } from '@/state/session';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
const mockStored = new Map<string, string>();
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(async (key: string) => mockStored.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => void mockStored.set(key, value)),
  deleteItemAsync: jest.fn(),
}));
jest.mock('@/lib/api', () => ({
  scanner: { list: jest.fn() },
  proposals: { fromSignal: jest.fn(), get: jest.fn(), assess: jest.fn() },
  paper: { place: jest.fn() },
}));
const api = jest.requireMock('@/lib/api') as {
  scanner: { list: jest.Mock };
  proposals: Record<'fromSignal' | 'get' | 'assess', jest.Mock>;
  paper: { place: jest.Mock };
};
const ok = <T,>(data: T) => ({ data });

const ASSESSMENT = () => ({
  id: 'a1',
  proposalId: 'p1',
  quantity: '26',
  riskPerTradePct: '0.0050',
  quote: { last: '186.40', asOf: new Date().toISOString() },
  equity: '100000.00',
  capitalRequired: '4849.00',
  maxLoss: '215.80',
  potentialProfit: '455.00',
  checks: [{ code: 'risk_per_trade', status: 'pass', message: 'ok', actual: null, limit: null }],
  approvable: true,
  createdAt: new Date().toISOString(),
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  stale: false,
});
const PROPOSAL = () => ({
  id: 'p1',
  status: 'pending',
  symbol: 'NVDA',
  name: 'NVIDIA Corp.',
  orderType: 'buy_stop',
  entry: '186.50',
  stop: '178.20',
  target: '204.00',
  analysis: { headline: 'Breaking out of its base on strong volume.' },
  expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
  assessment: ASSESSMENT(),
});
const signal = (id: string, symbol: string, evidence: string, soaked: boolean) => ({ id, symbol, strategy: { evidence, soaked } });

async function renderGuided() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'guided-trade': GuidedTradeScreen,
      index: () => <Text>Home</Text>,
      activity: () => <Text>Activity screen</Text>,
    },
    { initialUrl: '/guided-trade' },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  mockStored.clear();
  useSession.setState({ user: { id: 'g-user' } as never });
  api.scanner.list.mockResolvedValue(ok({ items: [signal('s1', 'AMD', 'untested', false), signal('s2', 'NVDA', 'beats_market', true)] }));
  api.proposals.fromSignal.mockResolvedValue(ok(PROPOSAL()));
  api.proposals.get.mockResolvedValue(ok(PROPOSAL()));
  api.proposals.assess.mockResolvedValue(ok(ASSESSMENT()));
});

describe('Guided first trade (T-193)', () => {
  it('walks a real setup from "what is a stock" to approving with the hold, then says where to watch it', async () => {
    api.paper.place.mockResolvedValue(ok({ id: 'o1', status: 'working' }));
    await renderGuided();

    await fireEvent.press(await screen.findByRole('button', { name: 'Start' }));
    expect(api.proposals.fromSignal).toHaveBeenCalledWith('s2'); // the proven strategy's idea, not the higher-ranked untested one
    expect(await screen.findByText('This is NVIDIA Corp.')).toBeOnTheScreen();
    expect(screen.getByText('One share costs about 186.40 right now.')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('Buy only if the price rises to 186.50.')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText(/falls to 178\.20 after you buy, the shares sell automatically/)).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('You’d buy 26 shares for about $4,849.00.')).toBeOnTheScreen();
    expect(screen.getByText(/you’d lose about \$215\.80: 0\.22% of your \$100,000 practice money/)).toBeOnTheScreen();
    await waitFor(() => expect(api.proposals.assess).toHaveBeenCalledWith('p1', { riskPerTradePct: '0.0050' }, expect.anything()));

    await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
    await fireEvent.press(await screen.findByRole('button', { name: 'Review and approve' }));
    jest.spyOn(Alert, 'alert').mockImplementation((_t, _b, buttons) => buttons?.find((b) => b.text === 'Approve')?.onPress?.());
    await fireEvent(screen.getByTestId('hold-to-approve'), 'accessibilityAction', { nativeEvent: { actionName: 'activate' } });

    expect(api.paper.place).toHaveBeenCalledWith({ proposalId: 'p1', assessmentId: 'a1' }, '00000000-0000-4000-8000-000000000001');
    expect(await screen.findByText('Well done! Your first practice trade is in.')).toBeOnTheScreen();
    expect(screen.getByText(/It’s waiting for NVDA to reach 186\.50/)).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Watch it in Activity' }));
    await waitFor(() => expect(screen).toHavePathname('/activity'));
  });

  it('can be skipped and remembers the step', async () => {
    await renderGuided();
    await fireEvent.press(await screen.findByRole('button', { name: 'Start' }));
    await fireEvent.press(await screen.findByRole('button', { name: 'Next' }));
    expect(await screen.findByText('The plan')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Skip for now' }));
    await waitFor(() => expect(screen).toHavePathname('/'));
    expect(JSON.parse(mockStored.get('astryx.guidedTrade.v1.g-user')!)).toEqual({ step: 'plan', proposalId: 'p1' });
  });

  it('resumes where the user left off', async () => {
    mockStored.set('astryx.guidedTrade.v1.g-user', JSON.stringify({ step: 'plan', proposalId: 'p1' }));
    await renderGuided();

    expect(await screen.findByText('The plan')).toBeOnTheScreen();
    expect(screen.getByText('STEP 2 OF 5 · NVDA')).toBeOnTheScreen();
    expect(api.proposals.fromSignal).not.toHaveBeenCalled();
  });

  it('offers a new idea when the saved one has ended', async () => {
    mockStored.set('astryx.guidedTrade.v1.g-user', JSON.stringify({ step: 'risk', proposalId: 'old' }));
    api.proposals.get.mockResolvedValueOnce(ok({ ...PROPOSAL(), id: 'old', status: 'expired' }));
    await renderGuided();

    expect(await screen.findByText('This idea has ended')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Pick a new idea' }));
    expect(await screen.findByText('This is NVIDIA Corp.')).toBeOnTheScreen();
  });

  it('explains when there are no ideas today', async () => {
    api.scanner.list.mockResolvedValue(ok({ items: [] }));
    await renderGuided();
    await fireEvent.press(await screen.findByRole('button', { name: 'Start' }));

    expect(await screen.findByTestId('guided-problem')).toHaveTextContent(/no trade ideas right now/);
    expect(screen.getByRole('button', { name: 'Try again' })).toBeOnTheScreen();
  });
});
