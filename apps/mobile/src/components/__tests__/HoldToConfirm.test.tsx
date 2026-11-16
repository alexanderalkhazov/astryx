import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { Alert } from 'react-native';

import { HOLD_MS, HoldToConfirm } from '../HoldToConfirm';

const props = { confirmTitle: 'Approve simulated trade?', confirmBody: 'Buy 76 NVDA in your paper account.' };

beforeEach(() => jest.useFakeTimers());
afterEach(() => jest.useRealTimers());

describe('HoldToConfirm (design 07)', () => {
  it('does not fire at 1199 ms and fires exactly once at 1200 ms', async () => {
    const onConfirm = jest.fn();
    await render(<HoldToConfirm {...props} onConfirm={onConfirm} />);

    await fireEvent(screen.getByRole('button'), 'pressIn');
    expect(screen.getByText('Keep holding…')).toBeOnTheScreen();
    await act(() => jest.advanceTimersByTime(HOLD_MS - 1));
    expect(onConfirm).not.toHaveBeenCalled();
    await act(() => jest.advanceTimersByTime(1));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    await fireEvent(screen.getByRole('button'), 'pressOut');
    await fireEvent(screen.getByRole('button'), 'pressIn');
    await act(() => jest.advanceTimersByTime(HOLD_MS * 2));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('resets when released early', async () => {
    const onConfirm = jest.fn();
    await render(<HoldToConfirm {...props} onConfirm={onConfirm} />);

    await fireEvent(screen.getByRole('button'), 'pressIn');
    await act(() => jest.advanceTimersByTime(800));
    await fireEvent(screen.getByRole('button'), 'pressOut');
    await act(() => jest.advanceTimersByTime(HOLD_MS));

    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText('Hold to approve')).toBeOnTheScreen();
  });

  it('gives screen readers an Approve action behind a native confirmation', async () => {
    const onConfirm = jest.fn();
    const alert = jest
      .spyOn(Alert, 'alert')
      .mockImplementation((_t, _b, buttons) => buttons?.find((b) => b.text === 'Approve')?.onPress?.());
    await render(<HoldToConfirm {...props} onConfirm={onConfirm} />);

    await fireEvent(screen.getByRole('button'), 'accessibilityAction', { nativeEvent: { actionName: 'activate' } });

    expect(alert).toHaveBeenCalledWith('Approve simulated trade?', 'Buy 76 NVDA in your paper account.', expect.any(Array));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('ignores holds while busy and says it is sending', async () => {
    const onConfirm = jest.fn();
    await render(<HoldToConfirm {...props} busy onConfirm={onConfirm} />);

    await fireEvent(screen.getByRole('button'), 'pressIn');
    await act(() => jest.advanceTimersByTime(HOLD_MS));

    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText('Sending…')).toBeOnTheScreen();
  });
});
