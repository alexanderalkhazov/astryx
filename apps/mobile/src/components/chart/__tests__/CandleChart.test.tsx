import { render, screen } from '@testing-library/react-native';

import { CandleChart, summarize } from '../CandleChart';

const bars = Array.from({ length: 12 }, (_, i) => {
  const day = String(i + 1).padStart(2, '0');
  return { t: `2026-10-${day}T13:30:00Z`, d: `2026-10-${day}`, o: '100.00', h: `${110 + i}.00`, l: `${90 - i}.00`, c: `${100 + i}.50`, v: 1_000_000 };
});

describe('CandleChart', () => {
  it('summarises the visible bars for screen readers', () => {
    expect(summarize(bars)).toBe('Price chart, 12 bars from Oct 1 to Oct 12. Last close 111.50. Range 79.00 to 121.00.');
  });

  it('labels the chart and lists the last 10 bars newest first, skipping warm-up bars', async () => {
    await render(<CandleChart bars={bars} warmup={2} levels={{ entry: '186.50', stop: '178.20', target: '204.00' }} />);

    expect(screen.getByLabelText('Price chart, 10 bars from Oct 3 to Oct 12. Last close 111.50. Range 79.00 to 121.00.')).toBeOnTheScreen();
    const list = screen.getByTestId('candle-chart-last-bars');
    expect(list.children).toHaveLength(10);
    expect(screen.getByText('Oct 12: open 100.00, high 121.00, low 79.00, close 111.50, volume 1.0M')).toBeOnTheScreen();
  });
});
