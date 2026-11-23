import { checklist, localDate } from '../gettingStarted';

jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn() }));

const started = { firstSeen: '2026-11-24', lessonRead: false, dismissed: false };

describe('Getting started checklist (T-195)', () => {
  it('derives every item from the facts, nothing ticked by hand', () => {
    const none = checklist({ hasPlan: false, watching: false, traded: false, started, today: '2026-11-24' });
    expect(none.map((i) => [i.key, i.done])).toEqual([
      ['plan', false],
      ['watch', false],
      ['trade', false],
      ['lesson', false],
      ['return', false],
    ]);

    const all = checklist({ hasPlan: true, watching: true, traded: true, started: { ...started, lessonRead: true }, today: '2026-11-25' });
    expect(all.every((i) => i.done)).toBe(true);
  });

  it('counts "come back another day" only on a later local date', () => {
    const item = (today: string) => checklist({ hasPlan: false, watching: false, traded: false, started, today }).at(-1)!.done;
    expect(item('2026-11-24')).toBe(false);
    expect(item('2026-11-25')).toBe(true);
  });

  it('formats the local date', () => {
    expect(localDate(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05');
  });
});
