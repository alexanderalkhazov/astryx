import { type HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/react-query';
import { create } from 'zustand';

import { analysisKeys } from '@/features/analysis/keys';

export const HUB_PATH = '/hubs/workstation';

/** Delays before automatic reconnect attempts; after the last one the socket restarts every RESTART_MS. */
export const RECONNECT_DELAYS_MS = [0, 2_000, 10_000, 30_000];
export const RESTART_MS = 30_000;

/** `analysis.updated` (SYSTEM_DESIGN.md §9.6). */
export interface AnalysisUpdatedMessage {
  jobId: string;
  symbol: string;
  status: 'pending' | 'processing' | 'completed' | 'failed' | 'cancelled';
  attempt: number;
  resultId: string | null;
  errorCode: string | null;
}

/** Whether the socket is up. Pollers slow down while it is, and take over when it isn't. */
export const useRealtime = create<{ connected: boolean }>(() => ({ connected: false }));

export interface RealtimeOptions {
  baseUrl: string;
  /** A token valid for at least the next few seconds (refreshing if needed). */
  getAccessToken: () => Promise<string>;
  queryClient: QueryClient;
}

/** Applies a server event to the query cache: the event is a hint, the REST refetch is the truth. */
export function handleAnalysisUpdated(queryClient: QueryClient, message: AnalysisUpdatedMessage): void {
  void queryClient.invalidateQueries({ queryKey: analysisKeys.job(message.jobId) });
  if (message.status === 'completed') void queryClient.invalidateQueries({ queryKey: analysisKeys.list(message.symbol) });
}

/**
 * The workstation hub connection (§9.6): server-to-client events only. SignalR is an optimisation, so every failure
 * just leaves `connected` false and the pollers carry on; a closed socket is restarted while the session lasts.
 */
export function startRealtime({ baseUrl, getAccessToken, queryClient }: RealtimeOptions): () => void {
  let stopped = false;
  let restartTimer: ReturnType<typeof setTimeout> | undefined;
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(`${baseUrl}${HUB_PATH}`, { accessTokenFactory: getAccessToken })
    .withAutomaticReconnect(RECONNECT_DELAYS_MS)
    .configureLogging(LogLevel.None)
    .build();

  const setConnected = (connected: boolean) => useRealtime.setState({ connected });
  connection.on('analysis.updated', (message: AnalysisUpdatedMessage) => handleAnalysisUpdated(queryClient, message));
  connection.onreconnecting(() => setConnected(false));
  connection.onreconnected(() => {
    setConnected(true);
    // Events may have been missed while down: let every analysis query catch up.
    void queryClient.invalidateQueries({ queryKey: analysisKeys.all });
  });
  connection.onclose(() => {
    setConnected(false);
    scheduleRestart();
  });

  function scheduleRestart() {
    if (stopped) return;
    clearTimeout(restartTimer);
    restartTimer = setTimeout(() => void start(), RESTART_MS);
  }

  async function start() {
    if (stopped) return;
    try {
      await connection.start();
      if (stopped) {
        await connection.stop();
        return;
      }
      setConnected(true);
    } catch {
      setConnected(false);
      scheduleRestart();
    }
  }

  void start();
  return () => {
    stopped = true;
    clearTimeout(restartTimer);
    setConnected(false);
    void connection.stop();
  };
}
