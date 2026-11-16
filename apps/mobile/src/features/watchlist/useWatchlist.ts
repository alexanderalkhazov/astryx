import type { Watchlist, WatchlistItem } from '@astryx/api-client';
import { isApiError } from '@astryx/api-client';
import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { watchlists } from '@/lib/api';
import { describeError } from '@/lib/forms';
import { toast } from '@/state/toasts';

export const WATCHLISTS_KEY = ['watchlists'] as const;

const fetchLists = async () => (await watchlists.list()).data.items;
const selectDefault = (lists: Watchlist[]) => lists.find((l) => l.isDefault) ?? null;

/** The default list (the star toggle's list), or null before the first load. */
export function useDefaultWatchlist() {
  return useQuery({ queryKey: WATCHLISTS_KEY, queryFn: fetchLists, select: selectDefault });
}

/** Applies a change to the cached default list and returns what was there, for rollback. */
function patchDefault(queryClient: QueryClient, change: (list: Watchlist) => Watchlist): Watchlist[] | undefined {
  const previous = queryClient.getQueryData<Watchlist[]>(WATCHLISTS_KEY);
  queryClient.setQueryData<Watchlist[]>(WATCHLISTS_KEY, (lists) => lists?.map((l) => (l.isDefault ? change(l) : l)));
  return previous;
}

/** Shown until the refetch brings the name and price. */
const pendingItem = (symbol: string): WatchlistItem => ({
  symbol,
  name: symbol,
  sector: null,
  last: null,
  changePct: null,
  hasActiveSignal: false,
  held: false,
  note: null,
  addedAt: new Date().toISOString(),
});

/**
 * Star toggle for one symbol (SYSTEM_DESIGN.md §11.5 W6/W7). Optimistic: the star flips at once, rolls back with a
 * toast if the server refuses, and toggles of one symbol run in order.
 */
export function useWatchlistStar(symbol: string) {
  const queryClient = useQueryClient();
  const list = useDefaultWatchlist();
  const starred = list.data?.items.some((i) => i.symbol === symbol) ?? false;

  const mutation = useMutation({
    scope: { id: `watchlist-star-${symbol}` },
    mutationFn: async (on: boolean) => {
      try {
        await (on ? watchlists.star(symbol) : watchlists.unstar(symbol));
      } catch (error) {
        if (on && isApiError(error) && error.code === 'duplicate') return; // already on the list
        throw error;
      }
    },
    onMutate: async (on) => {
      await queryClient.cancelQueries({ queryKey: WATCHLISTS_KEY });
      const previous = patchDefault(queryClient, (l) => ({
        ...l,
        items: on ? [...l.items.filter((i) => i.symbol !== symbol), pendingItem(symbol)] : l.items.filter((i) => i.symbol !== symbol),
      }));
      return { previous };
    },
    onError: (error, on, context) => {
      queryClient.setQueryData(WATCHLISTS_KEY, context?.previous);
      toast({ tone: 'error', title: on ? "Couldn't add to watchlist" : "Couldn't remove from watchlist", body: describeError(error) });
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: WATCHLISTS_KEY }),
  });

  return { starred, ready: list.isSuccess, toggle: () => mutation.mutate(!starred) };
}

/** Remove and reorder on the Manage screen; both optimistic with rollback. */
export function useWatchlistEditor() {
  const queryClient = useQueryClient();
  const rollback = (title: string) => (error: unknown, _: unknown, context?: { previous?: Watchlist[] }) => {
    queryClient.setQueryData(WATCHLISTS_KEY, context?.previous);
    const stale = isApiError(error) && (error.status === 412 || error.status === 409);
    toast({ tone: 'error', title, body: stale ? 'Your watchlist changed elsewhere. It has been reloaded.' : describeError(error) });
  };
  const settle = () => queryClient.invalidateQueries({ queryKey: WATCHLISTS_KEY });

  const remove = useMutation({
    scope: { id: 'watchlist-edit' },
    mutationFn: async ({ list, symbol }: { list: Watchlist; symbol: string }) => {
      await watchlists.removeItem(list.id, symbol);
    },
    onMutate: async ({ symbol }) => {
      await queryClient.cancelQueries({ queryKey: WATCHLISTS_KEY });
      return { previous: patchDefault(queryClient, (l) => ({ ...l, items: l.items.filter((i) => i.symbol !== symbol) })) };
    },
    onError: rollback("Couldn't remove from watchlist"),
    onSettled: settle,
  });

  const reorder = useMutation({
    scope: { id: 'watchlist-edit' },
    // Reads the version at send time: an earlier edit in the same scope has bumped it by then.
    mutationFn: async ({ symbols }: { symbols: string[] }) => {
      const list = selectDefault(queryClient.getQueryData<Watchlist[]>(WATCHLISTS_KEY) ?? []);
      if (!list) throw new Error('No watchlist loaded');
      const { data } = await watchlists.reorder(list, symbols);
      queryClient.setQueryData<Watchlist[]>(WATCHLISTS_KEY, (lists) => lists?.map((l) => (l.id === data.id ? data : l)));
    },
    onMutate: async ({ symbols }) => {
      await queryClient.cancelQueries({ queryKey: WATCHLISTS_KEY });
      return {
        previous: patchDefault(queryClient, (l) => ({
          ...l,
          items: symbols.map((s) => l.items.find((i) => i.symbol === s)).filter((i): i is WatchlistItem => i !== undefined),
        })),
      };
    },
    onError: rollback("Couldn't reorder your watchlist"),
    onSettled: settle,
  });

  return {
    remove: (list: Watchlist, symbol: string) => remove.mutate({ list, symbol }),
    move: (list: Watchlist, from: number, to: number) => {
      if (to < 0 || to >= list.items.length) return;
      const symbols = list.items.map((i) => i.symbol);
      const [moved] = symbols.splice(from, 1);
      symbols.splice(to, 0, moved!);
      reorder.mutate({ symbols });
    },
  };
}
