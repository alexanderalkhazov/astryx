import { ApiError } from '@astryx/api-client';

import { applyServerErrors, describeError, emailField, newPasswordField } from '@/lib/forms';
import { currentHref } from '@/state/pendingLink';

describe('applyServerErrors', () => {
  it('maps 422 field errors onto form fields, whatever the server casing', () => {
    const setError = jest.fn();
    const error = new ApiError({ status: 422, code: 'validation_failed', errors: { NewPassword: ['Password must be 12–128 characters.'] } });

    expect(applyServerErrors(error, setError, { newPassword: 'password' })).toBeNull();
    expect(setError).toHaveBeenCalledWith('password', { type: 'server', message: 'Password must be 12–128 characters.' }, { shouldFocus: true });
  });

  it('returns errors with no matching field as a form message', () => {
    const setError = jest.fn();
    const error = new ApiError({ status: 422, code: 'validation_failed', errors: { userId: ['Malformed id.'], email: ['Enter a valid email address.'] } });

    expect(applyServerErrors(error, setError, { email: 'email' })).toBe('Malformed id.');
    expect(setError).toHaveBeenCalledTimes(1);
  });

  it('describes non-field failures', () => {
    const setError = jest.fn();
    expect(applyServerErrors(new ApiError({ status: 422, code: 'invalid_or_expired_token', detail: 'This reset link is invalid or has expired.' }), setError, {})).toBe(
      'This reset link is invalid or has expired.',
    );
    expect(describeError(new ApiError({ status: 0, code: 'network_error' }))).toMatch(/Can't reach Astryx/);
    expect(describeError(new ApiError({ status: 429, code: 'rate_limited', detail: 'Too Many Requests' }))).toMatch(/Too many attempts/);
    expect(describeError(new ApiError({ status: 503, code: 'unavailable' }))).toMatch(/having trouble/);
    expect(describeError(new Error('boom'))).toBe('Something went wrong. Try again.');
    expect(setError).not.toHaveBeenCalled();
  });
});

describe('form schemas mirror the server validators', () => {
  it('trims and validates emails', () => {
    expect(emailField.parse('  trader@example.com ')).toBe('trader@example.com');
    expect(emailField.safeParse('').error?.issues[0]?.message).toBe('Enter your email.');
    expect(emailField.safeParse('trader@').error?.issues[0]?.message).toBe('Enter a valid email address.');
  });

  it('requires 12–128 character passwords', () => {
    expect(newPasswordField.safeParse('short').success).toBe(false);
    expect(newPasswordField.safeParse('x'.repeat(12)).success).toBe(true);
    expect(newPasswordField.safeParse('x'.repeat(129)).success).toBe(false);
  });
});

describe('currentHref', () => {
  it('keeps search params but not dynamic segments already in the path', () => {
    expect(currentHref('/stock/NVDA', ['(app)', 'stock', '[ticker]'], { ticker: 'NVDA', tab: 'news' })).toBe('/stock/NVDA?tab=news');
    expect(currentHref('/activity', ['(app)', '(tabs)', 'activity'], { filter: 'orders' })).toBe('/activity?filter=orders');
    expect(currentHref('/strategy/abc', ['(app)', 'strategy', '[id]'], { id: 'abc' })).toBe('/strategy/abc');
  });
});
