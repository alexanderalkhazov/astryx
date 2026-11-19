import { Text } from 'react-native';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';

import AppLayout from '../(app)/_layout';
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

const routes = {
  '(app)/_layout': AppLayout,
  '(app)/index': () => <Text>Home</Text>,
  '(app)/welcome': WelcomeScreen,
  privacy: PrivacyScreen,
};

const signedInAs = (id: string) => {
  useSession.getState().signIn('access-1', '2026-11-19T12:15:00Z');
  useSession.getState().setUser({ id, email: `${id}@example.com`, emailConfirmed: true } as never);
};

beforeEach(() => mockStored.clear());

describe('onboarding disclaimer (T-118)', () => {
  it('sends a new account to the disclaimer first and remembers the acceptance', async () => {
    signedInAs('user-new');
    await renderRouter(routes, { initialUrl: '/' });

    await waitFor(() => expect(screen).toHavePathname('/welcome'));
    expect(screen.getByLabelText(/^Paper trading only\./)).toBeOnTheScreen();
    expect(screen.getByLabelText(/^Not investment advice\./)).toBeOnTheScreen();
    expect(screen.getByLabelText(/^AI research is experimental\./)).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'I understand' }));

    await waitFor(() => expect(screen).toHavePathname('/'));
    expect(screen.getByText('Home')).toBeOnTheScreen();
    expect(mockStored.get('astryx.disclaimer.v1.user-new')).toBe('accepted');
  });

  it('lets an account that already accepted straight in', async () => {
    mockStored.set('astryx.disclaimer.v1.user-old', 'accepted');
    signedInAs('user-old');
    await renderRouter(routes, { initialUrl: '/' });

    expect(await screen.findByText('Home')).toBeOnTheScreen();
    expect(screen).toHavePathname('/');
  });

  it('shows the privacy policy', async () => {
    await renderRouter(routes, { initialUrl: '/privacy' });

    expect(screen.getByText('Privacy policy')).toBeOnTheScreen();
    expect(screen.getByText('Alpaca connection')).toBeOnTheScreen();
    expect(screen.getByText(/There are no ads and no analytics trackers\./)).toBeOnTheScreen();
  });
});
