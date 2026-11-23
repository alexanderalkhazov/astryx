import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import LearnScreen from '../(app)/(tabs)/learn';
import PracticeScreen from '../(app)/(tabs)/practice';
import { saveInvestorProfile } from '@/features/onboarding/profile';
import { useSession } from '@/state/session';

jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(async () => null),
  setItemAsync: jest.fn(async () => undefined),
  deleteItemAsync: jest.fn(),
}));
jest.mock('@/lib/api', () => ({ portfolio: { positions: jest.fn() }, scanner: { list: jest.fn() } }));
const api = jest.requireMock('@/lib/api') as { portfolio: { positions: jest.Mock }; scanner: { list: jest.Mock } };
const ok = <T,>(data: T) => ({ data });

const signal = (id: string, symbol: string, evidence: string, soaked: boolean) => ({
  id,
  symbol,
  type: 'breakout',
  score: '80',
  reason: `${symbol} setup`,
  rank: 1,
  name: `${symbol} Inc.`,
  sector: 'Technology',
  price: '100.00',
  changePct: null,
  sparkline: ['98.00', '100.00'],
  volume: '1200000',
  levels: { entry: '101.00', stop: '95.00', target: '115.00' },
  metrics: { rsi14: null, rewardRisk: '2.33' },
  strategy: { evidence, soaked },
});

async function renderAt(url: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      practice: PracticeScreen,
      learn: LearnScreen,
      'guided-trade': () => <Text>Guided trade</Text>,
      discover: () => <Text>Discover screen</Text>,
    },
    { initialUrl: url },
  );
}

beforeEach(async () => {
  jest.clearAllMocks();
  useSession.setState({ user: { id: 'p-user' } as never });
  await saveInvestorProfile('p-user', { level: 'beginner', horizon: 'years', tolerance: 'hold' });
  api.portfolio.positions.mockResolvedValue(ok({ items: [], nextCursor: null }));
  api.scanner.list.mockResolvedValue(ok({ items: [signal('s1', 'AMD', 'untested', false), signal('s2', 'NVDA', 'beats_market', true)] }));
});

describe('Practice tab (T-194)', () => {
  it('offers the guided trade and shows a beginner only ideas from tested strategies', async () => {
    await renderAt('/practice');

    expect(await screen.findByTestId('practice-guided')).toBeOnTheScreen();
    expect(await screen.findByText('NVDA setup')).toBeOnTheScreen();
    expect(screen.queryByText('AMD setup')).toBeNull();

    await fireEvent.press(screen.getByRole('button', { name: 'Start the guided trade' }));
    await waitFor(() => expect(screen).toHavePathname('/guided-trade'));
  });

  it('explains why a beginner sees no ideas yet', async () => {
    api.scanner.list.mockResolvedValue(ok({ items: [signal('s1', 'AMD', 'untested', false)] }));
    await renderAt('/practice');

    expect(await screen.findByTestId('practice-no-ideas')).toHaveTextContent(/needs a live trial before beginners see its ideas/);
  });
});

describe('Learn tab (T-194)', () => {
  it('opens a word to read it', async () => {
    await renderAt('/learn');

    expect(screen.queryByText(/sold automatically to cap the loss/)).toBeNull();
    await fireEvent.press(screen.getByRole('button', { name: 'Stop loss' }));
    expect(screen.getByText(/sold automatically to cap the loss/)).toBeOnTheScreen();
  });
});
