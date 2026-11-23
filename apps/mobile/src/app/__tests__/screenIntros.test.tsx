import fs from 'node:fs';
import path from 'node:path';

import { render, screen } from '@testing-library/react-native';
import { Text } from 'react-native';

import { Screen, ScreenIntroContext } from '@/components/Shell';
import { INTRO } from '@/features/plain/screenIntros';

jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn() }));

const APP = path.join(__dirname, '..', '(app)');

/** Screens whose whole content already explains itself in every mode, or that are not real screens yet. */
const EXEMPT: Record<string, string> = {
  'welcome.tsx': 'the disclaimer: its points are the explanation',
  'onboarding.tsx': 'the questions: the title is the question and the next line says why',
  'start.tsx': 'the goal choice: the title is the question and each choice explains itself',
  'guided-trade.tsx': 'every step is a plain explanation',
  'analyses/index.tsx': 'placeholder, development builds only',
  'analyses/[id].tsx': 'placeholder, development builds only',
};

function screens(dir: string, prefix = ''): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const rel = path.join(prefix, entry.name);
    if (entry.isDirectory()) return entry.name === 'dev' ? [] : screens(path.join(dir, entry.name), rel);
    return entry.name.endsWith('.tsx') && entry.name !== '_layout.tsx' ? [rel.replace(/^\(tabs\)\//, '')] : [];
  });
}

const source = (rel: string) => {
  const direct = path.join(APP, rel);
  return fs.readFileSync(fs.existsSync(direct) ? direct : path.join(APP, '(tabs)', rel), 'utf8');
};

describe('every Simple-mode screen says what it is and what to do (T-197)', () => {
  it.each(screens(APP).filter((s) => !(s in EXEMPT)))('%s has an intro sentence', (rel) => {
    expect(source(rel)).toMatch(/intro=\{INTRO\.\w+\}|<ScreenIntro>\{INTRO\.\w+\}<\/ScreenIntro>/);
  });

  it('uses every intro, and each is one or two short sentences', () => {
    const all = screens(APP).map(source).join('\n');
    for (const [key, text] of Object.entries(INTRO)) {
      expect(all).toContain(`INTRO.${key}`);
      expect(text.split(/[.!?](\s|$)/).filter((x) => x.trim().length > 1).length).toBeLessThanOrEqual(2);
      expect(text.length).toBeLessThanOrEqual(120);
    }
  });

  it('only lists exemptions that exist', () => {
    for (const rel of Object.keys(EXEMPT)) expect(screens(APP)).toContain(rel);
  });

  it('shows the intro in Simple mode only', async () => {
    const page = (simple: boolean) => (
      <ScreenIntroContext.Provider value={simple}>
        <Screen intro={INTRO.search}>
          <Text>Body</Text>
        </Screen>
      </ScreenIntroContext.Provider>
    );
    const { rerender } = await render(page(true));
    expect(screen.getByTestId('screen-intro')).toHaveTextContent(INTRO.search);

    await rerender(page(false));
    expect(screen.queryByTestId('screen-intro')).toBeNull();
  });
});
