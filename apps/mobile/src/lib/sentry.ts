import * as Sentry from '@sentry/react-native';

/**
 * Error tracking (SYSTEM_DESIGN.md §23.5, T-113): on only when `EXPO_PUBLIC_SENTRY_DSN` is set at build time.
 * No personal data: no user identity, IPs, request bodies or breadcrumb URLs with query strings, so balances, tokens
 * and eToro keys never leave the device in a report.
 */
export function initSentry(dsn = process.env.EXPO_PUBLIC_SENTRY_DSN): boolean {
  if (!dsn) return false;
  Sentry.init({
    dsn,
    sendDefaultPii: false,
    tracesSampleRate: 0,
    environment: __DEV__ ? 'development' : 'production',
    beforeBreadcrumb: (breadcrumb) =>
      breadcrumb.data?.url ? { ...breadcrumb, data: { ...breadcrumb.data, url: String(breadcrumb.data.url).split('?')[0] } } : breadcrumb,
  });
  return true;
}
