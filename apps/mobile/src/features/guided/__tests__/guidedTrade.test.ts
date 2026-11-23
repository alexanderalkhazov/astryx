import type { Signal } from '@astryx/api-client';

import { pickSetup } from '../guidedTrade';

jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn() }));

const s = (symbol: string, evidence: string, soaked: boolean) =>
  ({ id: symbol, symbol, strategy: { evidence, soaked } }) as unknown as Signal;

describe('pickSetup (T-193)', () => {
  it('prefers a proven, soaked strategy over a higher-ranked untested one', () => {
    expect(pickSetup([s('AMD', 'untested', false), s('NVDA', 'beats_market', true)])).toEqual({
      signal: s('NVDA', 'beats_market', true),
      proven: true,
    });
  });

  it('falls back to the top idea while no strategy has finished its trial', () => {
    expect(pickSetup([s('AMD', 'beats_market', false), s('NVDA', 'untested', false)])?.signal.symbol).toBe('AMD');
  });

  it('skips symbols already tried, and returns null when nothing is left', () => {
    expect(pickSetup([s('AMD', 'untested', false), s('NVDA', 'untested', false)], ['AMD'])?.signal.symbol).toBe('NVDA');
    expect(pickSetup([s('AMD', 'untested', false)], ['AMD'])).toBeNull();
  });
});
