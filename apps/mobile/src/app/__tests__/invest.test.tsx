import { ApiError, type InvestPlan } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';

import InvestScreen from '../(app)/(tabs)/invest';
import { useSession } from '@/state/session';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(async (key: string) =>
    key === 'astryx.profile.v1.inv-user' ? JSON.stringify({ level: 'beginner', horizon: 'years', tolerance: 'sell' }) : null,
  ),
  setItemAsync: jest.fn(),
  deleteItemAsync: jest.fn(),
}));
jest.mock('@/lib/api', () => ({ invest: { get: jest.fn(), save: jest.fn(), investCash: jest.fn(), rebalance: jest.fn() } }));
const api = (jest.requireMock('@/lib/api') as { invest: Record<'get' | 'save' | 'investCash' | 'rebalance', jest.Mock> }).invest;

const PLAN = (over: Partial<InvestPlan> = {}): InvestPlan => ({
  profile: 'cautious',
  value: '10250.00',
  cash: '0.42',
  contributed: '10000.00',
  gain: '250.00',
  gainPct: '0.0250',
  funds: [
    {
      symbol: 'SPY',
      name: 'S&P 500 (US stocks)',
      role: 'US stocks',
      targetWeight: '0.30',
      quantity: '5.1',
      price: '601.20',
      value: '3066.12',
      weight: '0.2991',
      costBasis: '3000.00',
    },
    {
      symbol: 'VXUS',
      name: 'International stocks',
      role: 'International stocks',
      targetWeight: '0.10',
      quantity: '14.0',
      price: '71.40',
      value: '999.60',
      weight: '0.0975',
      costBasis: '1000.00',
    },
    {
      symbol: 'BND',
      name: 'US bonds',
      role: 'Bonds',
      targetWeight: '0.60',
      quantity: '84.5',
      price: '73.20',
      value: '6185.40',
      weight: '0.6034',
      costBasis: '6000.00',
    },
  ],
  maxDrift: '0.0034',
  rebalanceSuggested: false,
  contribution: { amount: '200.00', frequency: 'monthly', nextOn: '2026-12-23' },
  goal: { amount: '50000.00', date: null, progress: '0.2050' },
  activity: [{ kind: 'deposit', symbol: null, quantity: null, price: null, amount: '10000.00', occurredAt: '2026-11-23T15:00:00Z' }],
  pricesAsOf: '2026-11-23T15:00:00Z',
  ...over,
});

function renderInvest() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      invest: InvestScreen,
    },
    { initialUrl: '/invest' },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  useSession.setState({ user: { id: 'inv-user' } as never });
});

describe('Investing (T-164)', () => {
  it('starts a plan with the mix the onboarding answers suggest', async () => {
    api.get.mockRejectedValue(new ApiError({ status: 404, code: 'not_found', detail: 'No plan.' }));
    api.save.mockResolvedValue({ data: PLAN() });
    await renderInvest();

    expect(await screen.findByText('Invest for the long term')).toBeOnTheScreen();
    await waitFor(() => expect(screen.getByRole('radio', { name: 'Cautious, 40% stocks · 60% bonds', selected: true })).toBeOnTheScreen()); // would sell after a 10 % drop
    await fireEvent.press(screen.getByRole('button', { name: 'Start investing' }));

    await waitFor(() =>
      expect(api.save).toHaveBeenCalledWith(
        { profile: 'cautious', initialDeposit: '10000', contributionAmount: '200', contributionFrequency: 'monthly', goalAmount: null },
        '00000000-0000-4000-8000-000000000001',
      ),
    );
    expect(await screen.findByTestId('invest-plan')).toBeOnTheScreen();
  });

  it('shows the plan against its target mix, its contributions and goal', async () => {
    api.get.mockResolvedValue({ data: PLAN() });
    await renderInvest();

    expect(await screen.findByLabelText('Investing plan · Cautious: 10250 dollars and 0 cents')).toBeOnTheScreen();
    expect(screen.getByLabelText('US bonds: 60% now, target 60%')).toBeOnTheScreen();
    expect(screen.getByTestId('invest-contribution')).toHaveTextContent('$200.00 every month · next on Dec 23');
    expect(screen.getByTestId('invest-goal-progress')).toHaveTextContent(/20% of the way there/);
    expect(screen.queryByTestId('invest-rebalance-suggested')).not.toBeOnTheScreen();
  });

  it('offers to rebalance once the mix drifts, and does it', async () => {
    api.get.mockResolvedValue({ data: PLAN({ rebalanceSuggested: true, maxDrift: '0.1200' }) });
    api.rebalance.mockResolvedValue({ data: PLAN() });
    await renderInvest();

    expect(await screen.findByTestId('invest-rebalance-suggested')).toHaveTextContent(/drifted 12% from its target/);
    await fireEvent.press(screen.getByRole('button', { name: 'Rebalance' }));

    await waitFor(() => expect(api.rebalance).toHaveBeenCalledWith('00000000-0000-4000-8000-000000000001'));
    await waitFor(() => expect(screen.queryByTestId('invest-rebalance-suggested')).not.toBeOnTheScreen());
  });
});
