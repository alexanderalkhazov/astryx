import { color } from '@astryx/design-tokens';
import { render, screen } from '@testing-library/react-native';
import { StyleSheet } from 'react-native';

import { Pnl, Text } from '../Text';

describe('Pnl', () => {
  it('renders losses with a minus sign in the loss colour', async () => {
    await render(<Pnl value="-630.80" testID="pnl" />);

    const node = screen.getByTestId('pnl');
    expect(node).toHaveTextContent('−$630.80');
    expect(StyleSheet.flatten(node.props.style).color).toBe(color.loss);
    expect(node.props.accessibilityLabel).toBe('minus 630 dollars and 80 cents');
  });

  it('renders gains with a plus sign in the gain colour', async () => {
    await render(<Pnl value="1330" testID="pnl" />);

    const node = screen.getByTestId('pnl');
    expect(node).toHaveTextContent('+$1,330.00');
    expect(StyleSheet.flatten(node.props.style).color).toBe(color.gain);
  });

  it('renders zero as neutral', async () => {
    await render(<Pnl value="0" testID="pnl" />);

    expect(StyleSheet.flatten(screen.getByTestId('pnl').props.style).color).toBe(color.text2);
  });
});

describe('Text', () => {
  it('applies the design type scale and tabular figures to data text', async () => {
    await render(<Text.Data testID="price">186.40</Text.Data>);

    const style = StyleSheet.flatten(screen.getByTestId('price').props.style);
    expect(style.fontFamily).toBe('GeistMono_500Medium');
    expect(style.fontVariant).toEqual(['tabular-nums']);
    expect(style.color).toBe(color.text);
  });

  it('maps tones to semantic colours', async () => {
    await render(
      <Text.Overline tone="pending" testID="band">
        Not executed
      </Text.Overline>,
    );

    const style = StyleSheet.flatten(screen.getByTestId('band').props.style);
    expect(style.color).toBe(color.pending);
    expect(style.fontFamily).toBe('GeistMono_600SemiBold');
  });
});
