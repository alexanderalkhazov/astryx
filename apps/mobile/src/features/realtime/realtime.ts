import { type HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/react-query';
import { create } from 'zustand';

import { analysisKeys } from '@/features/analysis/keys';
import { formatMoney, formatPnl, formatQuantity } from '@/lib/format';
import { toast } from '@/state/toasts';

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

/** `order.updated` (§9.6). Quantity and price are set when it filled. */
export interface OrderUpdatedMessage {
  orderId: string;
  proposalId: string | null;
  symbol: string;
  status: 'accepted' | 'working' | 'filled' | 'cancelled' | 'expired' | 'rejected';
  purpose: string;
  quantity: string | null;
  price: string | null;
}

/** `position.updated` (§9.6). */
export interface PositionUpdatedMessage {
  positionId: string;
  symbol: string;
  status: 'open' | 'closed';
  exitReason: 'stop' | 'target' | 'manual' | 'reset' | null;
  price: string | null;
  realizedPnl: string | null;
  gappedThroughStop: boolean;
}

/** Queries that money movements change; refetched on every order or position event. */
export const MONEY_QUERY_KEYS = [['portfolio'], ['positions'], ['activity'], ['proposal']] as const;

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

export function handleOrderUpdated(queryClient: QueryClient, message: OrderUpdatedMessage): void {
  for (const key of MONEY_QUERY_KEYS) void queryClient.invalidateQueries({ queryKey: key });
  if (message.status === 'filled' && message.price && message.quantity && message.purpose === 'entry') {
    toast({ tone: 'success', title: 'Filled (simulated)', body: `Bought ${formatQuantity(message.quantity)} ${message.symbol} at ${formatMoney(message.price)}` });
  } else if (message.status === 'expired') {
    toast({ tone: 'info', title: `Order expired · ${message.symbol}`, body: 'The buy stop was not triggered before the close. Its cash is released.' });
  }
}

export function handlePositionUpdated(queryClient: QueryClient, message: PositionUpdatedMessage): void {
  for (const key of MONEY_QUERY_KEYS) void queryClient.invalidateQueries({ queryKey: key });
  if (message.status !== 'closed' || !message.price) return;
  const pnl = message.realizedPnl ? ` · P&L ${formatPnl(message.realizedPnl)}` : '';
  if (message.exitReason === 'stop') {
    toast({
      tone: 'warning',
      title: `Stop hit · ${message.symbol}`,
      body: `Sold at ${formatMoney(message.price)}${pnl}${message.gappedThroughStop ? '. Stop filled below plan (gap).' : ''}`,
    });
  } else if (message.exitReason === 'target') {
    toast({ tone: 'success', title: `Target hit · ${message.symbol}`, body: `Sold at ${formatMoney(message.price)}${pnl}` });
  }
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
  connection.on('order.updated', (message: OrderUpdatedMessage) => handleOrderUpdated(queryClient, message));
  connection.on('position.updated', (message: PositionUpdatedMessage) => handlePositionUpdated(queryClient, message));
  connection.onreconnecting(() => setConnected(false));
  connection.onreconnected(() => {
    setConnected(true);
    // Events may have been missed while down: let every analysis and money query catch up.
    void queryClient.invalidateQueries({ queryKey: analysisKeys.all });
    for (const key of MONEY_QUERY_KEYS) void queryClient.invalidateQueries({ queryKey: key });
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
