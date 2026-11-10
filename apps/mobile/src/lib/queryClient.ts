import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 1 },
    // Money-moving mutations never retry automatically; retries reuse the same Idempotency-Key (SYSTEM_DESIGN.md §8.7).
    mutations: { retry: 0 },
  },
});
