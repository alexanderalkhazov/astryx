import { color } from '@astryx/design-tokens';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { BackHandler, StyleSheet } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { useSheetStore } from '@/state/sheets';
import { MAX_TOASTS, useToasts } from '@/state/toasts';

import { CheckRow } from '../CheckRow';
import { Dialog } from '../Dialog';
import { MetricGrid, MetricHero, MetricTile, ScoreBreakdownBar } from '../Metric';
import { Sheet, sheetBehaviour } from '../Sheet';
import { StatusBanner } from '../StatusBanner';
import { ToastHost } from '../ToastHost';

const flat = (node: { props: object }) => StyleSheet.flatten((node.props as { style?: unknown }).style as never) as Record<string, unknown>;

describe('StatusBanner', () => {
  it('shows the order ID on executed banners', async () => {
    await render(<StatusBanner tone="executed" title="Executed · simulated fill" body="Bought 76 NVDA at $186.52" orderId="SIM-463AC2" />);

    expect(screen.getByText('EXECUTED · SIMULATED FILL')).toBeOnTheScreen();
    expect(screen.getByText('Order SIM-463AC2')).toBeOnTheScreen();
    expect(flat(screen.getByText('EXECUTED · SIMULATED FILL')).color).toBe(color.gain);
  });

  it('renders research as an amber dashed banner without any order ID', async () => {
    await render(
      <StatusBanner tone="pending" title="Research proposal — not executed" body="Nothing is sent until you hold to approve." testID="b" />,
    );

    const banner = screen.getByTestId('b');
    expect(flat(banner).borderStyle).toBe('dashed');
    expect(flat(screen.getByText('RESEARCH PROPOSAL — NOT EXECUTED')).color).toBe(color.pending);
    expect(screen.queryByText(/Order /)).toBeNull();
  });

  it('announces errors as alerts', async () => {
    await render(
      <StatusBanner tone="error" title="Connection lost" body="Approvals are disabled." action={{ label: 'Retry', onPress: () => {} }} />,
    );

    expect(screen.getByRole('alert')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeOnTheScreen();
  });
});

describe('CheckRow', () => {
  it('names the status for screen readers and colours failures', async () => {
    await render(
      <>
        <CheckRow status="pass" label="Risk per trade · 0.53%" first />
        <CheckRow status="warn" label="Technology 22.4% → 34.4%" />
        <CheckRow status="fail" label="Position weight" detail="12.15% · limit 12%" />
      </>,
    );

    expect(screen.getByLabelText('Passed: Risk per trade · 0.53%')).toBeOnTheScreen();
    expect(screen.getByLabelText('Warning: Technology 22.4% → 34.4%')).toBeOnTheScreen();
    expect(screen.getByLabelText('Failed: Position weight. 12.15% · limit 12%')).toBeOnTheScreen();
    expect(flat(screen.getByText('12.15% · limit 12%')).color).toBe(color.loss);
  });
});

describe('Metrics', () => {
  it('renders the hero with dimmed cents', async () => {
    await render(<MetricHero label="Total portfolio value" value="118181.30" />);

    expect(screen.getByText('.30')).toBeOnTheScreen();
    expect(flat(screen.getByText('.30')).color).toBe(color.text3);
    expect(screen.getByLabelText('Total portfolio value: 118181 dollars and 30 cents')).toBeOnTheScreen();
  });

  it('renders tiles, grids and score bars', async () => {
    await render(
      <>
        <MetricTile label="Max potential loss" value="−$630.80" tone="loss" caption="0.53% of equity" />
        <MetricGrid
          items={[
            { label: 'CAGR', value: '+16.9%' },
            { label: 'Sharpe', value: '1.31' },
          ]}
        />
        <ScoreBreakdownBar label="Pattern quality" value="24" max="30" />
      </>,
    );

    expect(flat(screen.getByText('−$630.80')).color).toBe(color.loss);
    expect(screen.getByLabelText('Sharpe: 1.31')).toBeOnTheScreen();
    expect(screen.getByLabelText('Pattern quality: 24 of 30')).toBeOnTheScreen();
  });
});

describe('Dialog', () => {
  it('puts cancel first and confirms with the destructive action', async () => {
    const onCancel = jest.fn();
    const onConfirm = jest.fn();
    await render(
      <Dialog
        visible
        title="Close NVDA position?"
        body="Sell 76 NVDA at market."
        overline="Simulated · paper account"
        cancelLabel="Keep open"
        confirmLabel="Close position"
        destructive
        onCancel={onCancel}
        onConfirm={onConfirm}
      />,
    );

    const buttons = screen.getAllByRole('button');
    expect(buttons.map((b) => b.props.accessibilityLabel)).toEqual(['Keep open', 'Close position']);
    expect(flat(screen.getByRole('button', { name: 'Close position' })).backgroundColor).toBe(color.loss);
    await fireEvent.press(screen.getByRole('button', { name: 'Close position' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Keep open' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(screen.getByText('SIMULATED · PAPER ACCOUNT')).toBeOnTheScreen();
  });

  it('cannot be cancelled while busy', async () => {
    const onCancel = jest.fn();
    await render(
      <Dialog
        visible
        busy
        title="Closing"
        body="…"
        cancelLabel="Keep open"
        confirmLabel="Close"
        onCancel={onCancel}
        onConfirm={() => {}}
      />,
    );

    await fireEvent.press(screen.getByRole('button', { name: 'Keep open' }));

    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Close' })).toBeBusy();
  });
});

describe('Sheet', () => {
  it('locks every dismissal path while not dismissible', () => {
    const locked = sheetBehaviour(false);
    const dismiss = jest.fn();

    expect(locked.enablePanDownToClose).toBe(false);
    expect(locked.backdropPressBehavior).toBe('none');
    expect(locked.onHardwareBack(dismiss)).toBe(true); // back is swallowed...
    expect(dismiss).not.toHaveBeenCalled(); // ...and the sheet stays
  });

  it('allows dismissal by default', () => {
    const open = sheetBehaviour(true);
    const dismiss = jest.fn();

    expect(open.enablePanDownToClose).toBe(true);
    expect(open.backdropPressBehavior).toBe('close');
    expect(open.onHardwareBack(dismiss)).toBe(true);
    expect(dismiss).toHaveBeenCalled();
  });

  it('swallows Android back while an order is sending', async () => {
    const remove = jest.fn();
    let handler: (() => boolean) | undefined;
    jest.spyOn(BackHandler, 'addEventListener').mockImplementation((_event, cb) => {
      handler = cb as () => boolean;
      return { remove };
    });
    const onClose = jest.fn();

    await render(
      <Sheet open dismissible={false} onClose={onClose} accessibilityLabel="Confirm order">
        <></>
      </Sheet>,
    );

    expect(handler?.()).toBe(true);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('opens one sheet at a time through the store', () => {
    useSheetStore.getState().open('filters');
    useSheetStore.getState().open('confirm-order');
    useSheetStore.getState().close('filters'); // closing a sheet that isn't active does nothing

    expect(useSheetStore.getState().active).toBe('confirm-order');
    useSheetStore.getState().close();
    expect(useSheetStore.getState().active).toBeNull();
  });
});

describe('Toasts', () => {
  // No component is mounted while the store changes, so these updates need no act(). (In RNTL 14 a synchronous act()
  // before an async render leaves the next render empty.)
  beforeEach(() => {
    jest.useFakeTimers();
    useToasts.getState().clear();
  });

  afterEach(() => jest.useRealTimers());

  it('auto-dismisses after 3.8 s', () => {
    useToasts.getState().show({ tone: 'success', title: 'Simulated order filled' });
    jest.advanceTimersByTime(3799);
    expect(useToasts.getState().toasts).toHaveLength(1);

    jest.advanceTimersByTime(1);
    expect(useToasts.getState().toasts).toHaveLength(0);
  });

  it('keeps at most three, dropping the oldest', () => {
    for (let i = 1; i <= 4; i++) useToasts.getState().show({ tone: 'info', title: `Toast ${i}` });

    expect(useToasts.getState().toasts.map((t) => t.title)).toEqual(['Toast 2', 'Toast 3', 'Toast 4']);
    expect(MAX_TOASTS).toBe(3);
  });

  it('renders at the top and dismisses on tap', async () => {
    jest.useRealTimers(); // async render flushes fake timers, which would fire the 3.8 s auto-dismiss first
    useToasts.getState().show({ tone: 'success', title: 'Simulated order filled', body: 'Bought 76 NVDA at $186.52' });
    // The app root provides safe areas; without a provider useSafeAreaInsets throws.
    await render(
      <SafeAreaProvider
        initialMetrics={{ frame: { x: 0, y: 0, width: 390, height: 844 }, insets: { top: 47, left: 0, right: 0, bottom: 34 } }}
      >
        <ToastHost />
      </SafeAreaProvider>,
    );

    const card = screen.getByRole('alert');
    expect(card.props.accessibilityLabel).toBe('Simulated order filled. Bought 76 NVDA at $186.52');
    await fireEvent.press(card);

    expect(useToasts.getState().toasts).toHaveLength(0);
  });
});
