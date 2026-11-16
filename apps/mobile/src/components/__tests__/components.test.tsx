import { color } from '@astryx/design-tokens';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { Search } from 'lucide-react-native';
import { StyleSheet } from 'react-native';

import { Badge, SignalBadge } from '../Badge';
import { Button, buttonStyle, IconButton } from '../Button';
import { Chip, RemovableChip } from '../Chip';
import { EmptyState } from '../Feedback';
import { Input, Select } from '../Input';
import { StackHeader, TabBar, type TabBarProps } from '../Navigation';
import { Segmented } from '../Segmented';
import { PaperChip } from '../Shell';
import { Stepper } from '../Stepper';
import { Switch } from '../Switch';
import { Tabs } from '../Tabs';

jest.mock('expo-router', () => ({ useRouter: () => ({ back: jest.fn() }) }));

const flat = (node: { props: object }) => StyleSheet.flatten((node.props as { style?: unknown }).style as never) as Record<string, unknown>;

describe('Button', () => {
  it('fires onPress when enabled', async () => {
    const onPress = jest.fn();
    await render(<Button label="Review proposal" onPress={onPress} />);

    await fireEvent.press(screen.getByRole('button', { name: 'Review proposal' }));

    expect(onPress).toHaveBeenCalledTimes(1);
  });

  it('does not fire when disabled and reports the disabled state', async () => {
    const onPress = jest.fn();
    await render(<Button label="Approve simulated trade" disabled onPress={onPress} />);

    const button = screen.getByRole('button', { name: 'Approve simulated trade' });
    await fireEvent.press(button);

    expect(onPress).not.toHaveBeenCalled();
    expect(button).toBeDisabled();
    expect(flat(button).backgroundColor).toBe(color.surface2);
  });

  it('is busy and disabled while loading', async () => {
    const onPress = jest.fn();
    await render(<Button label="Sending" loading onPress={onPress} />);

    const button = screen.getByRole('button', { name: 'Sending' });
    await fireEvent.press(button);

    expect(onPress).not.toHaveBeenCalled();
    expect(button).toBeBusy();
  });

  it('dims while pressed', async () => {
    await render(<Button label="Primary" testOnly_pressed onPress={() => {}} />);

    expect(flat(screen.getByRole('button', { name: 'Primary' })).opacity).toBe(0.85);
  });

  it('maps each variant to the design colours', () => {
    expect(buttonStyle('primary', { pressed: false, disabled: false }).container.backgroundColor).toBe(color.text);
    expect(buttonStyle('primary', { pressed: false, disabled: false }).label.color).toBe(color.bg);
    expect(buttonStyle('destructive', { pressed: false, disabled: false }).label.color).toBe(color.loss);
    expect(buttonStyle('secondary', { pressed: false, disabled: false }).container.borderColor).toBe(color.lineStrong);
    expect(buttonStyle('link', { pressed: false, disabled: false }).label.color).toBe(color.signal);
    expect(buttonStyle('destructive', { pressed: false, disabled: true }).label.color).toBe(color.text3);
  });
});

describe('IconButton', () => {
  it('is named for screen readers and dims when pressed', async () => {
    const onPress = jest.fn();
    await render(<IconButton icon={Search} accessibilityLabel="Search" testOnly_pressed onPress={onPress} />);

    const button = screen.getByRole('button', { name: 'Search' });
    await fireEvent.press(button);

    expect(onPress).toHaveBeenCalled();
    expect(flat(button).opacity).toBe(0.6);
  });
});

describe('Badges', () => {
  it('uses the design colour per signal type, none of them P&L green or red', async () => {
    await render(
      <>
        <SignalBadge type="breakout" />
        <SignalBadge type="momentum" />
        <SignalBadge type="unusual_volume" />
      </>,
    );

    for (const label of ['Breakout', 'Momentum', 'Unusual volume']) {
      const text = flat(screen.getByText(label));
      expect([color.gain, color.loss]).not.toContain(text.color);
    }

    expect(flat(screen.getByText('Breakout')).color).toBe(color.signal);
  });

  it('renders status badges in mono uppercase', async () => {
    await render(
      <>
        <Badge label="1.2% to stop" tone="pending" mono />
        <PaperChip />
      </>,
    );

    expect(screen.getByText('1.2% TO STOP')).toBeOnTheScreen();
    expect(screen.getByText('PAPER')).toBeOnTheScreen();
  });
});

describe('Chips', () => {
  it('reports selection and includes the count in its label', async () => {
    const onPress = jest.fn();
    await render(<Chip label="Breakouts" count={2} selected onPress={onPress} />);

    const chip = screen.getByRole('button', { name: 'Breakouts, 2' });
    await fireEvent.press(chip);

    expect(chip).toBeSelected();
    expect(onPress).toHaveBeenCalled();
  });

  it('removes an active filter', async () => {
    const onRemove = jest.fn();
    await render(<RemovableChip label="Mega cap" onRemove={onRemove} />);

    await fireEvent.press(screen.getByRole('button', { name: 'Remove filter Mega cap' }));

    expect(onRemove).toHaveBeenCalled();
  });
});

describe('Segmented', () => {
  const options = [
    { value: '3M', label: '3M' },
    { value: '1Y', label: '1Y' },
    { value: '5D', label: '5D', disabled: true },
  ] as const;

  it('changes value, ignores the current one and skips disabled options', async () => {
    const onChange = jest.fn();
    await render(<Segmented accessibilityLabel="Timeframe" options={options} value="3M" onChange={onChange} />);

    await fireEvent.press(screen.getByRole('radio', { name: '3M' }));
    await fireEvent.press(screen.getByRole('radio', { name: '5D' }));
    await fireEvent.press(screen.getByRole('radio', { name: '1Y' }));

    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('1Y');
    expect(screen.getByRole('radio', { name: '3M' })).toBeSelected();
    expect(screen.getByRole('radio', { name: '5D' })).toBeDisabled();
  });
});

describe('Tabs', () => {
  it('selects a tab', async () => {
    const onChange = jest.fn();
    await render(
      <Tabs
        tabs={[
          { value: 'signal', label: 'Signal' },
          { value: 'news', label: 'News' },
        ]}
        value="signal"
        onChange={onChange}
      />,
    );

    await fireEvent.press(screen.getByRole('tab', { name: 'News' }));

    expect(onChange).toHaveBeenCalledWith('news');
    expect(screen.getByRole('tab', { name: 'Signal' })).toBeSelected();
  });
});

describe('Stepper', () => {
  it('steps with decimal arithmetic', async () => {
    const onChange = jest.fn();
    await render(<Stepper value="76" min="1" max="106" unit="shares" onChange={onChange} />);

    await fireEvent.press(screen.getByRole('button', { name: 'Increase shares' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Decrease shares' }));

    expect(onChange).toHaveBeenNthCalledWith(1, '77');
    expect(onChange).toHaveBeenNthCalledWith(2, '75');
  });

  it('disables the buttons at the bounds', async () => {
    const onChange = jest.fn();
    await render(<Stepper value="1" min="1" max="1" unit="shares" onChange={onChange} />);

    await fireEvent.press(screen.getByRole('button', { name: 'Increase shares' }));
    await fireEvent.press(screen.getByRole('button', { name: 'Decrease shares' }));

    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Increase shares' })).toBeDisabled();
  });

  it('supports screen-reader increment and decrement', async () => {
    const onChange = jest.fn();
    await render(<Stepper value="76" unit="shares" onChange={onChange} />);

    const stepper = screen.getByRole('adjustable');
    await fireEvent(stepper, 'accessibilityAction', { nativeEvent: { actionName: 'increment' } });

    expect(onChange).toHaveBeenCalledWith('77');
    expect(stepper.props.accessibilityValue).toEqual({ text: '76 shares' });
  });
});

describe('Switch', () => {
  it('toggles and reports its checked state', async () => {
    const onValueChange = jest.fn();
    await render(<Switch value={false} onValueChange={onValueChange} accessibilityLabel="Fills" />);

    const control = screen.getByRole('switch', { name: 'Fills' });
    await fireEvent.press(control);

    expect(onValueChange).toHaveBeenCalledWith(true);
    expect(control).not.toBeChecked();
  });

  it('does nothing when disabled', async () => {
    const onValueChange = jest.fn();
    await render(<Switch value onValueChange={onValueChange} accessibilityLabel="Push" disabled />);

    await fireEvent.press(screen.getByRole('switch', { name: 'Push' }));

    expect(onValueChange).not.toHaveBeenCalled();
  });
});

describe('Input and Select', () => {
  it('shows a field error', async () => {
    await render(<Input label="Email" error="Enter a valid email address." />);

    expect(screen.getByText('Enter a valid email address.')).toBeOnTheScreen();
    expect(screen.getByLabelText('Email').props.accessibilityHint).toBe('Enter a valid email address.');
  });

  it('opens, selects an option and closes', async () => {
    const onChange = jest.fn();
    const options = [
      { value: 'buy_stop', label: 'Buy stop · day' },
      { value: 'market', label: 'Market' },
    ] as const;
    await render(<Select label="Order type" options={options} value="buy_stop" onChange={onChange} />);

    await fireEvent.press(screen.getByRole('button', { name: 'Order type: Buy stop · day' }));
    await fireEvent.press(screen.getByRole('menuitem', { name: 'Market' }));

    expect(onChange).toHaveBeenCalledWith('market');
    expect(screen.queryByRole('menuitem', { name: 'Market' })).toBeNull();
  });
});

describe('EmptyState', () => {
  it('explains the emptiness and offers one action', async () => {
    const onPress = jest.fn();
    await render(
      <EmptyState title="No setups match these filters" body="Reset to see all setups." action={{ label: 'Reset filters', onPress }} />,
    );

    await fireEvent.press(screen.getByRole('button', { name: 'Reset filters' }));

    expect(screen.getByRole('header', { name: 'No setups match these filters' })).toBeOnTheScreen();
    expect(onPress).toHaveBeenCalled();
  });
});

describe('StackHeader', () => {
  it('goes back', async () => {
    const onBack = jest.fn();
    await render(<StackHeader title="Trade proposal" onBack={onBack} />);

    await fireEvent.press(screen.getByRole('button', { name: 'Back' }));

    expect(onBack).toHaveBeenCalled();
    expect(screen.getByRole('header', { name: 'Trade proposal' })).toBeOnTheScreen();
  });
});

describe('TabBar', () => {
  function props(overrides: Partial<{ index: number; defaultPrevented: boolean }> = {}) {
    const routes = ['index', 'discover', 'portfolio', 'strategies', 'activity'].map((name) => ({ key: `${name}-key`, name }));
    const titles: Record<string, string> = {
      index: 'Home',
      discover: 'Discover',
      portfolio: 'Portfolio',
      strategies: 'Strategies',
      activity: 'Activity',
    };
    const descriptors = Object.fromEntries(
      routes.map((r) => [r.key, { options: { title: titles[r.name], tabBarBadge: r.name === 'discover' ? 7 : undefined } }]),
    );
    const emitter = { emit: jest.fn(() => ({ defaultPrevented: overrides.defaultPrevented ?? false })) };
    const navigateToTab = jest.fn();
    return {
      value: {
        state: { index: overrides.index ?? 0, routes },
        descriptors,
        emitter,
        navigateToTab,
        insets: { top: 0, bottom: 34, left: 0, right: 0 },
      } as unknown as TabBarProps,
      emitter,
      navigateToTab,
    };
  }

  it('navigates to another tab and marks the active one by colour', async () => {
    const p = props();
    await render(<TabBar {...p.value} />);

    await fireEvent.press(screen.getByRole('tab', { name: 'Portfolio' }));

    expect(p.navigateToTab).toHaveBeenCalledWith('portfolio-key');
    expect(screen.getByRole('tab', { name: 'Home' })).toBeSelected();
    expect(flat(screen.getByText('Home')).color).toBe(color.text);
    expect(flat(screen.getByText('Portfolio')).color).toBe(color.text3);
  });

  it('shows the Discover count badge and does not re-navigate to the active tab', async () => {
    const p = props();
    await render(<TabBar {...p.value} />);

    await fireEvent.press(screen.getByRole('tab', { name: 'Home' }));

    expect(screen.getByRole('tab', { name: 'Discover, 7' })).toBeOnTheScreen();
    expect(p.emitter.emit).toHaveBeenCalledWith({ type: 'tabPress', target: 'index-key', canPreventDefault: true });
    expect(p.navigateToTab).not.toHaveBeenCalled();
  });

  it('respects a prevented tab press', async () => {
    const p = props({ defaultPrevented: true });
    await render(<TabBar {...p.value} />);

    await fireEvent.press(screen.getByRole('tab', { name: 'Activity' }));

    expect(p.navigateToTab).not.toHaveBeenCalled();
  });
});
