import { Text } from 'react-native';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';

import AppLayout from '../(app)/_layout';
import OnboardingScreen from '../(app)/onboarding';
import StartScreen from '../(app)/start';
import WelcomeScreen from '../(app)/welcome';
import PrivacyScreen from '../privacy';
import { useSession } from '@/state/session';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
const mockStored = new Map<string, string>();
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'WHEN_UNLOCKED_THIS_DEVICE_ONLY',
  getItemAsync: jest.fn(async (key: string) => mockStored.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string) => void mockStored.set(key, value)),
  deleteItemAsync: jest.fn(async (key: string) => void mockStored.delete(key)),
}));
jest.mock('@/features/realtime/useRealtimeConnection', () => ({ useRealtimeConnection: () => undefined }));
jest.mock('@/features/push/usePushNotifications', () => ({ usePushNotifications: () => undefined }));
jest.mock('@/features/security/useScreenProtection', () => ({ useScreenProtection: () => undefined }));
jest.mock('@/lib/api', () => ({ settings: { risk: jest.fn(), updateRisk: jest.fn() } }));
const api = jest.requireMock('@/lib/api') as { settings: { risk: jest.Mock; updateRisk: jest.Mock } };

const routes = {
  '(app)/_layout': AppLayout,
  '(app)/index': () => <Text>Home</Text>,
  '(app)/welcome': WelcomeScreen,
  '(app)/onboarding': OnboardingScreen,
  '(app)/start': StartScreen,
  '(app)/invest': () => <Text>Invest</Text>,
  '(app)/discover': () => <Text>Discover</Text>,
  privacy: PrivacyScreen,
};

const signedInAs = (id: string) => {
  useSession.getState().signIn('access-1', '2026-11-19T12:15:00Z');
  useSession.getState().setUser({ id, email: `${id}@example.com`, emailConfirmed: true } as never);
};

beforeEach(() => {
  mockStored.clear();
  api.settings.risk.mockResolvedValue({ data: { version: 3 } });
  api.settings.updateRisk.mockResolvedValue({ data: { version: 4 } });
});

describe('onboarding disclaimer (T-118)', () => {
  it('sends a new account to the disclaimer first and remembers the acceptance', async () => {
    signedInAs('user-new');
    await renderRouter(routes, { initialUrl: '/' });

    await waitFor(() => expect(screen).toHavePathname('/welcome'));
    expect(screen.getByLabelText(/^Paper trading only\./)).toBeOnTheScreen();
    expect(screen.getByLabelText(/^Not investment advice\./)).toBeOnTheScreen();
    expect(screen.getByLabelText(/^AI research is experimental\./)).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'I understand' }));

    await waitFor(() => expect(screen).toHavePathname('/onboarding')); // then the questions (T-162)
    expect(mockStored.get('astryx.disclaimer.v1.user-new')).toBe('accepted');
  });

  it('turns the answers into an experience level, its risk limits and the explanation mode (T-162)', async () => {
    mockStored.set('astryx.disclaimer.v1.user-beg', 'accepted');
    signedInAs('user-beg');
    await renderRouter(routes, { initialUrl: '/' });
    await waitFor(() => expect(screen).toHavePathname('/onboarding'));
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();

    await fireEvent.press(screen.getByRole('radio', { name: "I've bought a few, but don't trade" }));
    await fireEvent.press(screen.getByRole('radio', { name: 'Years: saving for the long term' }));
    await fireEvent.press(screen.getByRole('radio', { name: 'Sell, I can’t stand losing money' })); // one level more careful
    expect(screen.getByTestId('onboarding-summary')).toHaveTextContent(
      'Beginner: at most 0.50% of your account at risk per trade and 5 open positions. You can change this in Settings.',
    );
    await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(screen).toHavePathname('/start')); // then what to do first (T-190)
    expect(api.settings.updateRisk).toHaveBeenCalledWith(
      { version: 3 },
      expect.objectContaining({ maxRiskPerTradePct: '0.005', maxOpenPositions: 5 }),
    );
    expect(JSON.parse(mockStored.get('astryx.profile.v1.user-beg')!)).toEqual({ level: 'beginner', horizon: 'years', tolerance: 'sell' });
    expect(mockStored.get('astryx.viewMode.user-beg')).toBe('simple');
  });

  it('gives an experienced trader the default limits and the Pro view (T-162)', async () => {
    mockStored.set('astryx.disclaimer.v1.user-pro', 'accepted');
    signedInAs('user-pro');
    await renderRouter(routes, { initialUrl: '/' });
    await waitFor(() => expect(screen).toHavePathname('/onboarding'));

    await fireEvent.press(screen.getByRole('radio', { name: 'I trade regularly' }));
    await fireEvent.press(screen.getByRole('radio', { name: 'Days to weeks: active trading' }));
    await fireEvent.press(screen.getByRole('radio', { name: 'See it as normal ups and downs' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Continue' }));

    await waitFor(() => expect(screen).toHavePathname('/start')); // then what to do first (T-190)
    expect(api.settings.updateRisk).toHaveBeenCalledWith(
      { version: 3 },
      expect.objectContaining({ maxRiskPerTradePct: '0.0075', maxOpenPositions: 12 }),
    );
    expect(mockStored.get('astryx.viewMode.user-pro')).toBe('pro');
  });

  it('lets an account that already accepted and answered straight in', async () => {
    mockStored.set('astryx.disclaimer.v1.user-old', 'accepted');
    mockStored.set('astryx.profile.v1.user-old', JSON.stringify({ level: 'intermediate', horizon: 'months', tolerance: 'hold' }));
    mockStored.set('astryx.goal.v1.user-old', 'practice');
    signedInAs('user-old');
    await renderRouter(routes, { initialUrl: '/' });

    expect(await screen.findByText('Home')).toBeOnTheScreen();
    expect(screen).toHavePathname('/');
  });

  it('asks an account that answered the questions what it wants to do, once (T-190)', async () => {
    mockStored.set('astryx.disclaimer.v1.user-goal', 'accepted');
    mockStored.set('astryx.profile.v1.user-goal', JSON.stringify({ level: 'beginner', horizon: 'years', tolerance: 'hold' }));
    signedInAs('user-goal');
    await renderRouter(routes, { initialUrl: '/' });

    await waitFor(() => expect(screen).toHavePathname('/start'));
    expect(screen.getByRole('header', { name: 'What would you like to do?' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Practice trading with pretend money' })).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Just look around' })).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Grow my money for the long term' }));

    await waitFor(() => expect(screen).toHavePathname('/invest'));
    expect(mockStored.get('astryx.goal.v1.user-goal')).toBe('invest');
  });

  it('shows the privacy policy', async () => {
    await renderRouter(routes, { initialUrl: '/privacy' });

    expect(screen.getByText('Privacy policy')).toBeOnTheScreen();
    expect(screen.getByText('Alpaca connection')).toBeOnTheScreen();
    expect(screen.getByText(/There are no ads and no analytics trackers\./)).toBeOnTheScreen();
  });
});
