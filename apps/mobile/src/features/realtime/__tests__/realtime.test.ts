import { QueryClient } from '@tanstack/react-query';

import { analysisKeys } from '@/features/analysis/keys';

import { RESTART_MS, startRealtime, useRealtime } from '../realtime';

type Handler = (...args: unknown[]) => void;

const mockHub = {
  handlers: {} as Record<string, Handler>,
  onclose: undefined as Handler | undefined,
  onreconnecting: undefined as Handler | undefined,
  onreconnected: undefined as Handler | undefined,
  start: jest.fn<Promise<void>, []>(),
  stop: jest.fn<Promise<void>, []>(),
  url: '',
  options: undefined as { accessTokenFactory: () => Promise<string> } | undefined,
};

jest.mock('@microsoft/signalr', () => ({
  LogLevel: { None: 6 },
  HubConnectionBuilder: jest.fn().mockImplementation(() => {
    const builder = {
      withUrl: (url: string, options: typeof mockHub.options) => {
        mockHub.url = url;
        mockHub.options = options;
        return builder;
      },
      withAutomaticReconnect: () => builder,
      configureLogging: () => builder,
      build: () => ({
        on: (name: string, handler: Handler) => (mockHub.handlers[name] = handler),
        onclose: (h: Handler) => (mockHub.onclose = h),
        onreconnecting: (h: Handler) => (mockHub.onreconnecting = h),
        onreconnected: (h: Handler) => (mockHub.onreconnected = h),
        start: () => mockHub.start(),
        stop: () => mockHub.stop(),
      }),
    };
    return builder;
  }),
}));

const flush = () => new Promise((resolve) => setImmediate(resolve));

beforeEach(() => {
  jest.useRealTimers();
  mockHub.handlers = {};
  mockHub.start.mockReset().mockResolvedValue(undefined);
  mockHub.stop.mockReset().mockResolvedValue(undefined);
  useRealtime.setState({ connected: false });
});

function start(queryClient = new QueryClient()) {
  const getAccessToken = jest.fn().mockResolvedValue('token-1');
  const stop = startRealtime({ baseUrl: 'http://api.test', getAccessToken, queryClient });
  return { stop, queryClient, getAccessToken };
}

describe('realtime', () => {
  it('connects to the workstation mockHub with a fresh token and reports connected', async () => {
    const { stop, getAccessToken } = start();
    await flush();

    expect(mockHub.url).toBe('http://api.test/hubs/workstation');
    await expect(mockHub.options!.accessTokenFactory()).resolves.toBe('token-1');
    expect(getAccessToken).toHaveBeenCalled();
    expect(useRealtime.getState().connected).toBe(true);

    stop();
    expect(mockHub.stop).toHaveBeenCalled();
    expect(useRealtime.getState().connected).toBe(false);
  });

  it('turns analysis.updated into refetches of the job (and the list when completed)', async () => {
    const queryClient = new QueryClient();
    const invalidate = jest.spyOn(queryClient, 'invalidateQueries');
    const { stop } = start(queryClient);
    await flush();

    mockHub.handlers['analysis.updated']!({ jobId: 'j1', symbol: 'NVDA', status: 'processing', attempt: 1, resultId: null, errorCode: null });
    expect(invalidate).toHaveBeenLastCalledWith({ queryKey: analysisKeys.job('j1') });

    mockHub.handlers['analysis.updated']!({ jobId: 'j1', symbol: 'NVDA', status: 'completed', attempt: 1, resultId: 'r1', errorCode: null });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: analysisKeys.list('NVDA') });
    stop();
  });

  it('falls back to polling while reconnecting and catches up after', async () => {
    const queryClient = new QueryClient();
    const invalidate = jest.spyOn(queryClient, 'invalidateQueries');
    const { stop } = start(queryClient);
    await flush();

    mockHub.onreconnecting!();
    expect(useRealtime.getState().connected).toBe(false);
    mockHub.onreconnected!();
    expect(useRealtime.getState().connected).toBe(true);
    expect(invalidate).toHaveBeenCalledWith({ queryKey: analysisKeys.all });
    stop();
  });

  it('keeps retrying a failed or closed connection until stopped', async () => {
    jest.useFakeTimers();
    mockHub.start.mockRejectedValueOnce(new Error('down'));
    const { stop } = start();
    await Promise.resolve();
    await Promise.resolve();
    expect(useRealtime.getState().connected).toBe(false);

    await jest.advanceTimersByTimeAsync(RESTART_MS);
    expect(mockHub.start).toHaveBeenCalledTimes(2);
    expect(useRealtime.getState().connected).toBe(true);

    mockHub.onclose!();
    await jest.advanceTimersByTimeAsync(RESTART_MS);
    expect(mockHub.start).toHaveBeenCalledTimes(3);

    stop();
    mockHub.onclose!();
    await jest.advanceTimersByTimeAsync(RESTART_MS * 2);
    expect(mockHub.start).toHaveBeenCalledTimes(3);
  });
});
