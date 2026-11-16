import { ApiError, type AnalysisJob, type AnalysisResult } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';

import { useRealtime } from '@/features/realtime/realtime';

import { ANALYSIS_FAILED, AnalysisCard, CONFIDENCE_NOTE } from '../AnalysisCard';
import { useAnalysisJobs } from '../useAnalysis';

jest.mock('expo-crypto', () => ({ randomUUID: () => 'tap-key-0001' }));
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x',
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
  deleteItemAsync: jest.fn(),
}));
jest.mock('@/lib/api', () => ({ analyses: { create: jest.fn(), job: jest.fn(), cancel: jest.fn(), get: jest.fn(), list: jest.fn() } }));
const api = (jest.requireMock('@/lib/api') as { analyses: Record<'create' | 'job' | 'cancel' | 'get' | 'list', jest.Mock> }).analyses;

const job = (over: Partial<AnalysisJob> = {}): AnalysisJob => ({
  id: 'job-1',
  symbol: 'NVDA',
  kind: 'stock_analysis',
  status: 'pending',
  attempt: 0,
  createdAt: '2026-11-09T19:32:10Z',
  startedAt: null,
  completedAt: null,
  resultId: null,
  errorCode: null,
  errorDetail: null,
  pollAfterMs: 20,
  ...over,
});

const RESULT: AnalysisResult = {
  id: 'res-1',
  jobId: 'job-1',
  schemaVersion: 'analysis.v1',
  symbol: 'NVDA',
  analysisTimestamp: '2026-11-09T19:32:41Z',
  dataTimestamp: '2026-11-09T19:32:00Z',
  dataQuality: { status: 'ok', notes: [] },
  overallSentiment: 'bullish',
  scenarios: {
    bullish: { summary: 'Breakout holds above $185.90.', conditions: ['Closes above 185.90 for 2 sessions'] },
    bearish: { summary: 'Breakout fails back into the base.', conditions: ['Daily close below 182.00'] },
    neutral: { summary: 'Consolidates between 182 and 190.', conditions: ['Volume normalises'] },
  },
  supportingEvidence: ['Close above 6-week base high on 2.4× average volume'],
  contradictingEvidence: ['Earnings in 8 trading days'],
  keyRisks: ['Earnings gap through the stop'],
  relevantIndicators: [{ name: 'rsi14', value: '64.2', interpretation: 'Strong' }],
  catalysts: [{ type: 'earnings', date: '2026-11-19', note: 'Scheduled earnings report' }],
  confidence: { value: '0.55', explanation: 'Evidence mostly lines up.' },
  suggestedLevels: {
    justified: true,
    entryZone: { low: '186.00', high: '187.50' },
    stopLoss: '178.20',
    takeProfit: '204.00',
    rationale: 'Base midpoint.',
    accepted: true,
  },
  explanation: 'NVDA has broken out of a six-week base on heavy volume.',
  insufficientData: false,
  rewardRisk: '1.77',
  levelValidation: { accepted: true, reasons: [] },
  stale: false,
  model: 'qwen3.5:0.8b',
  promptVersion: 'stock_analysis/2026-11-09.1',
  disclaimer: 'AI-generated research for information only. Not investment advice.',
  validationStatus: 'valid',
};

const ok = <T,>(data: T) => ({ data });
const emptyPage = ok({ items: [], nextCursor: null });
const page = (r: AnalysisResult) => ok({ items: [{ id: r.id }], nextCursor: null });

let queryClient: QueryClient;

function renderCard() {
  // No garbage-collection timers, so Jest can exit when the tests finish.
  queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false, gcTime: Infinity } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <AnalysisCard symbol="NVDA" />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  useAnalysisJobs.setState({ bySymbol: {} });
  useRealtime.setState({ connected: false });
  api.list.mockResolvedValue(emptyPage);
  api.get.mockResolvedValue(ok(RESULT));
});

afterEach(() => queryClient?.clear());

describe('AnalysisCard', () => {
  it('requests an analysis, follows the job and renders the result (design: experimental, not a probability)', async () => {
    api.create.mockResolvedValue(ok(job()));
    api.job
      .mockResolvedValueOnce(ok(job({ status: 'processing', attempt: 1 })))
      .mockResolvedValue(ok(job({ status: 'completed', attempt: 1, resultId: 'res-1', pollAfterMs: null })));
    await renderCard();

    expect(screen.getByText('Experimental AI model')).toBeOnTheScreen();
    await fireEvent.press(await screen.findByRole('button', { name: 'Analyze with AI' }));

    expect(api.create).toHaveBeenCalledWith('NVDA', 'tap-key-0001');
    expect(await screen.findByText('Analyzing NVDA')).toBeOnTheScreen();
    api.list.mockResolvedValue(page(RESULT));

    // Poll → completed → result list → render: several async hops, slow when the whole suite runs in parallel.
    expect(await screen.findByTestId('analysis-result', {}, { timeout: 5000 })).toBeOnTheScreen();
    expect(screen.getByText('Bullish')).toBeOnTheScreen();
    expect(screen.getByText('Bullish case')).toBeOnTheScreen();
    expect(screen.getByText('Breakout fails back into the base.')).toBeOnTheScreen();
    expect(screen.getByText('Close above 6-week base high on 2.4× average volume')).toBeOnTheScreen();
    expect(screen.getByText('Earnings in 8 trading days')).toBeOnTheScreen();
    expect(screen.getByText('Earnings gap through the stop')).toBeOnTheScreen();
    expect(screen.getByText('Nov 19 · Scheduled earnings report')).toBeOnTheScreen();
    expect(screen.getByText('0.55')).toBeOnTheScreen();
    expect(screen.getByText(CONFIDENCE_NOTE)).toBeOnTheScreen();
    expect(screen.getByLabelText('R:R: 1.77')).toBeOnTheScreen();
    expect(screen.getByText('AI-generated research for information only. Not investment advice.')).toBeOnTheScreen();
    expect(screen.queryByTestId('analysis-progress')).not.toBeOnTheScreen();
  });

  it('shows the reliability message when the output was invalid, and lets the user try again', async () => {
    useAnalysisJobs.setState({ bySymbol: { NVDA: 'job-1' } });
    api.job.mockResolvedValue(
      ok(job({ status: 'failed', attempt: 1, errorCode: 'ai_output_invalid', errorDetail: 'x', pollAfterMs: null })),
    );
    api.create.mockResolvedValue(ok(job({ id: 'job-2' })));
    await renderCard();

    expect(await screen.findByText(ANALYSIS_FAILED)).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(api.create).toHaveBeenCalled());
    await waitFor(() => expect(useAnalysisJobs.getState().bySymbol.NVDA).toBe('job-2'));
  });

  it('follows the job already running when the request is a duplicate', async () => {
    api.create.mockRejectedValue(
      new ApiError({
        status: 409,
        code: 'duplicate',
        detail: 'An analysis for this symbol is already running.',
        extensions: { jobId: 'job-9' },
      }),
    );
    api.job.mockResolvedValue(ok(job({ id: 'job-9', status: 'processing', attempt: 2 })));
    await renderCard();

    await fireEvent.press(await screen.findByRole('button', { name: 'Analyze with AI' }));

    expect(await screen.findByText('Analyzing NVDA · attempt 2 of 3')).toBeOnTheScreen();
    expect(api.job).toHaveBeenCalledWith('job-9');
  });

  it('cancels a running job', async () => {
    useAnalysisJobs.setState({ bySymbol: { NVDA: 'job-1' } });
    api.job.mockResolvedValue(ok(job()));
    api.cancel.mockResolvedValue(ok(job({ status: 'cancelled', errorCode: 'cancelled', pollAfterMs: null })));
    await renderCard();

    await fireEvent.press(await screen.findByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(api.cancel).toHaveBeenCalledWith('job-1'));
    expect(await screen.findByRole('button', { name: 'Analyze with AI' })).toBeOnTheScreen();
  });

  it('explains rejected levels and marks stale analyses', async () => {
    api.list.mockResolvedValue(page(RESULT));
    api.get.mockResolvedValue(
      ok({
        ...RESULT,
        stale: true,
        rewardRisk: null,
        levelValidation: { accepted: false, reasons: ['Stop is too close (0.27× ATR). Minimum 0.5× ATR.'] },
        suggestedLevels: { ...RESULT.suggestedLevels, accepted: false },
      }),
    );
    await renderCard();

    expect(await screen.findByTestId('levels-rejected')).toBeOnTheScreen();
    expect(screen.getByText('Stop is too close (0.27× ATR). Minimum 0.5× ATR.')).toBeOnTheScreen();
    expect(screen.getByText('STALE')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Run a new analysis' })).toBeOnTheScreen();
  });

  it('says when the daily quota is used up', async () => {
    api.create.mockRejectedValue(
      new ApiError({ status: 429, code: 'ai_quota_exceeded', detail: "You've used today's AI analyses. Indicators are still available." }),
    );
    await renderCard();

    await fireEvent.press(await screen.findByRole('button', { name: 'Analyze with AI' }));

    expect(await screen.findByText("You've used today's AI analyses. Indicators are still available.")).toBeOnTheScreen();
  });

  it('polls slowly while the socket is connected', async () => {
    useRealtime.setState({ connected: true });
    useAnalysisJobs.setState({ bySymbol: { NVDA: 'job-1' } });
    api.job.mockResolvedValue(ok(job({ status: 'processing', attempt: 1 })));
    await renderCard();

    expect(await screen.findByText('Analyzing NVDA')).toBeOnTheScreen();
    await new Promise((resolve) => setTimeout(resolve, 150)); // pollAfterMs is 20 ms; the safety poll is 15 s
    expect(api.job).toHaveBeenCalledTimes(1);
  });
});
