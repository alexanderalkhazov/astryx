import { render, waitFor } from '@testing-library/react-native';
import { Text } from 'react-native';

import { Sheet } from '../Sheet';

// A stand-in BottomSheetModal that records present()/dismiss() calls.
const mockPresent = jest.fn();
const mockDismiss = jest.fn();
jest.mock('@gorhom/bottom-sheet', () => {
  const { View } = jest.requireActual('react-native');
  const { forwardRef, useImperativeHandle } = jest.requireActual('react');
  return {
    BottomSheetModal: forwardRef(({ children }: { children: React.ReactNode }, ref) => {
      useImperativeHandle(ref, () => ({ present: mockPresent, dismiss: mockDismiss }));
      return <View>{children}</View>;
    }),
    BottomSheetView: ({ children }: { children: React.ReactNode }) => <>{children}</>,
    BottomSheetBackdrop: () => null,
  };
});

beforeEach(() => jest.clearAllMocks());

describe('Sheet presenting', () => {
  it('never dismisses a sheet that mounts closed, so a later present() works', async () => {
    const view = await render(
      <Sheet open={false} onClose={() => undefined} accessibilityLabel="Test">
        <Text>Body</Text>
      </Sheet>,
    );
    await new Promise((resolve) => setTimeout(resolve, 50)); // past the deferred frame

    expect(mockDismiss).not.toHaveBeenCalled();

    await view.rerender(
      <Sheet open onClose={() => undefined} accessibilityLabel="Test">
        <Text>Body</Text>
      </Sheet>,
    );
    await waitFor(() => expect(mockPresent).toHaveBeenCalledTimes(1));

    await view.rerender(
      <Sheet open={false} onClose={() => undefined} accessibilityLabel="Test">
        <Text>Body</Text>
      </Sheet>,
    );
    await waitFor(() => expect(mockDismiss).toHaveBeenCalledTimes(1));
  });
});
