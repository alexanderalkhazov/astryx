import { create } from 'zustand';

/**
 * A deep link opened without a session (SYSTEM_DESIGN.md §8.3): stashed by the `(app)` gate and replayed once after
 * sign-in. Memory only, so a cold start never replays a link from an earlier run.
 */
export const usePendingLink = create<{ href: string | null }>(() => ({ href: null }));

export function stashPendingLink(href: string): void {
  // The home tab is where sign-in lands anyway.
  usePendingLink.setState({ href: href === '/' ? null : href });
}

export function clearPendingLink(): void {
  usePendingLink.setState({ href: null });
}

/**
 * Rebuilds the href of the current screen. Expo Router merges dynamic segments (`[ticker]`) into the params; those
 * are already in the pathname, so only the remaining params become the query string.
 */
export function currentHref(pathname: string, segments: readonly string[], params: Record<string, string | string[] | undefined>): string {
  const dynamic = new Set(segments.filter((s) => s.startsWith('[')).map((s) => s.replace(/^\[(\.\.\.)?|\]$/g, '')));
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (dynamic.has(key) || value === undefined) continue;
    for (const v of Array.isArray(value) ? value : [value]) query.append(key, v);
  }
  const qs = query.toString();
  return qs ? `${pathname}?${qs}` : pathname;
}
