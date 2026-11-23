import { fireEvent, render, screen } from '@testing-library/react-native';

import { SafetyBadge, WhatIf } from '../WhatIf';
import { setViewMode } from '@/features/plain/mode';
import { useSession } from '@/state/session';

jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(async () => null),
  setItemAsync: jest.fn(async () => undefined),
  deleteItemAsync: jest.fn(),
}));

beforeEach(async () => {
  useSession.setState({ user: { id: 'whatif-user' } as never });
  await setViewMode('whatif-user', 'simple');
});
afterEach(() => useSession.setState({ user: null }));

describe('Safety promise (T-192)', () => {
  it('says practice money can’t cost real money, in Simple mode only', async () => {
    const { rerender } = await render(<SafetyBadge />);
    expect(screen.getByLabelText('Practice money: nothing here can cost you real money')).toBeOnTheScreen();

    await setViewMode('whatif-user', 'pro');
    await rerender(<SafetyBadge />);
    expect(screen.queryByTestId('safety-badge')).toBeNull();
  });

  it('is not shown when signed out', async () => {
    useSession.setState({ user: null });
    await render(<SafetyBadge />);
    expect(screen.queryByTestId('safety-badge')).toBeNull();
  });
});

describe('What happens if…? (T-192)', () => {
  it.each(['approve', 'close', 'invest', 'rebalance'] as const)('answers "can I undo it?" for %s when opened', async (action) => {
    await render(<WhatIf action={action} />);
    expect(screen.queryByText('Can I undo it?')).toBeNull();

    await fireEvent.press(screen.getByRole('button', { name: 'What happens if…?' }));

    expect(screen.getByText('Can I undo it?')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Hide answers' })).toBeOnTheScreen();
  });

  it('tells a new trader what a falling price means before they approve', async () => {
    await render(<WhatIf action="approve" />);
    await fireEvent.press(screen.getByRole('button', { name: 'What happens if…?' }));

    expect(screen.getByText(/the shares sell automatically/)).toBeOnTheScreen();
    expect(screen.getByText(/The order keeps working on the server/)).toBeOnTheScreen();
  });
});
