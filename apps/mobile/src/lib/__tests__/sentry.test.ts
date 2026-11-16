import * as Sentry from '@sentry/react-native';

import { initSentry } from '../sentry';

const init = Sentry.init as jest.Mock;

beforeEach(() => init.mockClear());

describe('initSentry', () => {
  it('stays off without a DSN', () => {
    expect(initSentry('')).toBe(false);
    expect(init).not.toHaveBeenCalled();
  });

  it('starts without personal data and strips query strings from breadcrumbs', () => {
    expect(initSentry('https://key@o0.ingest.sentry.io/1')).toBe(true);
    const options = init.mock.calls[0][0];
    expect(options).toMatchObject({ dsn: 'https://key@o0.ingest.sentry.io/1', sendDefaultPii: false, tracesSampleRate: 0 });
    expect(options.beforeBreadcrumb({ category: 'fetch', data: { url: 'http://api/positions?token=secret' } }).data.url).toBe('http://api/positions');
  });
});
