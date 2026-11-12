import NetInfo from '@react-native-community/netinfo';
import { onlineManager, QueryClient } from '@tanstack/react-query';

// Offline pauses queries and resumes them on reconnect (SYSTEM_DESIGN.md §8.11).
onlineManager.setEventListener((setOnline) => NetInfo.addEventListener((state) => setOnline(state.isConnected !== false)));

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: 1 },
    // Money-moving mutations never retry automatically; retries reuse the same Idempotency-Key (SYSTEM_DESIGN.md §8.7).
    mutations: { retry: 0 },
  },
});
