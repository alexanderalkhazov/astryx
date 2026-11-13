import { BackHandler, Platform, Pressable, Text } from 'react-native';
import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { useState } from 'react';

import AppLayout from '../(app)/_layout';
import TabsLayout from '../(app)/(tabs)/_layout';
import ActivityScreen from '../(app)/(tabs)/activity';
import DiscoverScreen from '../(app)/(tabs)/discover';
import HomeScreen from '../(app)/(tabs)/index';
import PortfolioScreen from '../(app)/(tabs)/portfolio';
import StrategiesScreen from '../(app)/(tabs)/strategies';
import AnalysisScreen from '../(app)/analyses/[id]';
import AnalysesScreen from '../(app)/analyses/index';
import PositionScreen from '../(app)/position/[ticker]';
import ProposalScreen from '../(app)/proposal/[id]';
import SearchScreen from '../(app)/search';
import SettingsScreen from '../(app)/settings/index';
import WatchlistScreen from '../(app)/settings/watchlist';
import StrategyScreen from '../(app)/strategy/[id]';
import { Sheet } from '@/components/Sheet';
import { stackTransition, tabTransition } from '@/lib/motion';
import { useSession } from '@/state/session';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@astryx/api-client', () => ({ ...jest.requireActual('@astryx/api-client'), getSystemStatus: jest.fn(() => new Promise(() => {})) }));

/** A stock screen with a sheet open on top of it, to check Android back order. */
function StockWithSheet() {
  const [open, setOpen] = useState(true);
  return (
    <>
      <Text>Stock with sheet</Text>
      <Pressable accessibilityRole="button" accessibilityLabel="Close sheet" onPress={() => setOpen(false)} />
      <Sheet open={open} onClose={() => setOpen(false)} accessibilityLabel="Indicators">
        <Text>Indicators</Text>
      </Sheet>
    </>
  );
}

const queryClient = new QueryClient();

const routes = {
  _layout: () => (
    <QueryClientProvider client={queryClient}>
      <Slot />
    </QueryClientProvider>
  ),
  '(app)/_layout': AppLayout,
  '(app)/(tabs)/_layout': TabsLayout,
  '(app)/(tabs)/index': HomeScreen,
  '(app)/(tabs)/discover': DiscoverScreen,
  '(app)/(tabs)/portfolio': PortfolioScreen,
  '(app)/(tabs)/strategies': StrategiesScreen,
  '(app)/(tabs)/activity': ActivityScreen,
  '(app)/stock/[ticker]': StockWithSheet,
  '(app)/proposal/[id]': ProposalScreen,
  '(app)/position/[ticker]': PositionScreen,
  '(app)/strategy/[id]': StrategyScreen,
  '(app)/settings/index': SettingsScreen,
  '(app)/settings/watchlist': WatchlistScreen,
  '(app)/analyses/index': AnalysesScreen,
  '(app)/analyses/[id]': AnalysisScreen,
  '(app)/search': SearchScreen,
  '(auth)/sign-in': () => <Text>Sign in</Text>,
};

beforeEach(() => {
  useSession.setState({ status: 'signedIn', accessToken: 'a', accessTokenExpiresAt: null, user: null, signedOutByUser: false });
});

describe('app shell', () => {
  it('shows the Home header with the PAPER chip, sample-data notice and actions', async () => {
    await renderRouter(routes, { initialUrl: '/' });

    expect(screen.getByRole('header', { name: 'Overview' })).toBeOnTheScreen();
    expect(screen.getByTestId('paper-chip')).toBeOnTheScreen();
    expect(screen.getByText('Illustrative sample data — not live prices')).toBeOnTheScreen();
    expect(screen.getByText('Updated 14:32 ET')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Alerts, unread' })).toBeOnTheScreen();
  });

  it('opens Settings from the Home header', async () => {
    await renderRouter(routes, { initialUrl: '/' });

    await fireEvent.press(screen.getByRole('button', { name: 'Settings' }));

    await waitFor(() => expect(screen).toHavePathname('/settings'));
    expect(screen.getByRole('header', { name: 'Settings' })).toBeOnTheScreen();
  });

  it('opens Activity filtered to alerts from the bell', async () => {
    await renderRouter(routes, { initialUrl: '/' });

    await fireEvent.press(screen.getByRole('button', { name: 'Alerts, unread' }));

    await waitFor(() => expect(screen).toHavePathname('/activity'));
    expect(screen.getByText('filter=alerts')).toBeOnTheScreen();
  });

  it('switches between all five tabs, with the PAPER chip on Portfolio', async () => {
    await renderRouter(routes, { initialUrl: '/' });

    for (const [tab, pathname, title] of [
      ['discover', '/discover', 'Discover'],
      ['portfolio', '/portfolio', 'Portfolio'],
      ['strategies', '/strategies', 'Strategy lab'],
      ['activity', '/activity', 'Activity'],
      ['index', '/', 'Overview'],
    ] as const) {
      await fireEvent.press(screen.getByTestId(`tab-${tab}`));
      await waitFor(() => expect(screen).toHavePathname(pathname));
      expect(screen.getByTestId(`tab-${tab}`).props.accessibilityState).toMatchObject({ selected: true });
      expect(screen.getByRole('header', { name: title })).toBeOnTheScreen();
      if (tab === 'portfolio') expect(screen.getByTestId('paper-chip')).toBeOnTheScreen();
    }
  });

  it.each([
    ['Stock · NVDA', '/stock/NVDA', 'Stock with sheet'],
    ['Position · NVDA', '/position/NVDA', 'Position · NVDA'],
    ['Strategy', '/strategy/demo', 'Strategy · demo'],
    ['Analysis history', '/analyses', 'Analysis history'],
    ['Analysis', '/analyses/demo', 'Analysis · demo'],
    ['Watchlist (proposed)', '/settings/watchlist', 'Watchlist'],
  ])('pushes %s and goes back', async (link, pathname, title) => {
    await renderRouter(routes, { initialUrl: '/' });

    await fireEvent.press(screen.getByText(link));
    await waitFor(() => expect(screen).toHavePathname(pathname));
    expect(screen.getAllByText(title)[0]).toBeOnTheScreen();

    if (pathname !== '/stock/NVDA') {
      await fireEvent.press(screen.getByRole('button', { name: 'Back' }));
      await waitFor(() => expect(screen).toHavePathname('/'));
    }
  });

  it('opens Search from the Home header and closes it with Cancel', async () => {
    await renderRouter(routes, { initialUrl: '/' });
    await fireEvent.press(screen.getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(screen).toHavePathname('/search'));
    expect(screen.getByTestId('search-input')).toBeOnTheScreen();

    await fireEvent.press(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen).toHavePathname('/'));
  });
});

describe('Android back', () => {
  it('closes an open sheet before popping the screen', async () => {
    // Record every hardware-back listener (router and sheet) and call them newest first, as Android does.
    type Listener = Parameters<typeof BackHandler.addEventListener>[1];
    const listeners: Listener[] = [];
    jest.spyOn(BackHandler, 'addEventListener').mockImplementation((_event, handler) => {
      listeners.push(handler);
      return { remove: () => listeners.splice(listeners.indexOf(handler), 1) };
    });
    const pressBack = () => act(() => void [...listeners].reverse().some((handler) => handler({ type: 'hardwareBackPress', timeStamp: Date.now() }) === true));

    await renderRouter(routes, { initialUrl: '/' });
    await fireEvent.press(screen.getByText('Stock · NVDA'));
    await waitFor(() => expect(screen).toHavePathname('/stock/NVDA'));

    await pressBack();
    expect(screen).toHavePathname('/stock/NVDA'); // the sheet took it

    await fireEvent.press(screen.getByRole('button', { name: 'Close sheet' }));
    await pressBack();
    await waitFor(() => expect(screen).toHavePathname('/'));
  });
});

describe('transitions', () => {
  const original = Platform.OS;
  afterEach(() => {
    Platform.OS = original;
  });

  it('pushes from the right with edge-swipe back on iOS, and cross-fades under Reduce Motion', () => {
    Platform.OS = 'ios';
    expect(stackTransition(false)).toEqual({ animation: 'slide_from_right', animationDuration: 320, gestureEnabled: true });
    expect(stackTransition(true).animation).toBe('fade');
  });

  it('fades through on Android, and does not animate under Reduce Motion', () => {
    Platform.OS = 'android';
    expect(stackTransition(false).animation).toBe('fade');
    expect(stackTransition(true).animation).toBe('none');
  });

  it('fades and rises 6 pt between tabs in 200 ms, instantly under Reduce Motion', () => {
    expect(tabTransition(false).transitionSpec).toMatchObject({ animation: 'timing', config: { duration: 200 } });
    expect(tabTransition(true).transitionSpec).toMatchObject({ config: { duration: 0 } });
  });
});
