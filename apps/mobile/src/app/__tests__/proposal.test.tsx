import { ApiError, type Proposal, type RiskAssessment } from '@astryx/api-client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Slot } from 'expo-router';
import { fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';
import { Text } from 'react-native';

import ProposalScreen from '../(app)/proposal/[id]';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({ WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'x', getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
jest.mock('@/lib/api', () => ({ proposals: { get: jest.fn(), assess: jest.fn(), reject: jest.fn(), riskLimits: jest.fn() } }));
const api = (jest.requireMock('@/lib/api') as { proposals: Record<'get' | 'assess' | 'reject' | 'riskLimits', jest.Mock> }).proposals;

const inAMinute = () => new Date(Date.now() + 60_000).toISOString();

const check = (code: string, status: 'pass' | 'warn' | 'fail', message: string) => ({ code, status, message, actual: null, limit: null });

/** The §11.7 example assessment (design 06). */
const ASSESSMENT = (): RiskAssessment => ({
  id: 'a1', proposalId: 'p1', inputsHash: 'h', quantity: '76', sizedBy: 'weight_cap', limits: { byRisk: '106', byWeightCap: '76', byCash: '117' },
  riskPerTradePct: '0.0075', quote: { last: '186.40', asOf: new Date().toISOString() }, equity: '118181.30', cashAvailable: '21840.55',
  capitalRequired: '14174.00', maxLoss: '630.80', maxLossInclCosts: '633.84', potentialProfit: '1330.00', rewardRisk: '2.11', estFees: '0.00', estSlippage: '1.52',
  impact: { newWeightPct: '0.1199', sector: 'Semiconductors', sectorExposureAfterPct: '0.2410', cashAfter: '7665.03', openPositionsAfter: 6 },
  checks: [
    check('risk_per_trade', 'pass', 'Risk 0.54% ≤ 0.75% limit'),
    check('position_weight', 'warn', 'Weight 11.99%, close to 12% cap'),
    check('sector_exposure', 'pass', 'Semiconductors 24.1% ≤ 35%'),
    check('cash_available', 'pass', '$14,175.52 of $21,840.55 cash'),
    check('daily_loss_halt', 'pass', 'Today −0.12% vs −2.00% halt'),
    check('session_open', 'pass', 'Regular session open until 16:00 ET'),
    check('levels_valid', 'pass', 'Stop 1.50× ATR'),
    check('reward_risk', 'pass', 'Reward:risk 2.11 ≥ 1.50'),
  ],
  approvable: true, createdAt: new Date().toISOString(), expiresAt: inAMinute(), stale: false,
});

const PROPOSAL = (over: Partial<Proposal> = {}): Proposal => ({
  id: 'p1', status: 'pending', label: 'RESEARCH PROPOSAL · NOT EXECUTED', symbol: 'NVDA', name: 'NVIDIA Corp.', side: 'buy', direction: 'long', orderType: 'buy_stop', timeInForce: 'day',
  entry: '186.50', stop: '178.20', target: '204.00', stopPct: '-0.0445', targetPct: '0.0938',
  source: { type: 'signal', signalId: 's1', analysisId: null, strategyId: 'st', strategyName: 'Base Breakout v3', signalAt: '2026-11-09T19:32:00Z' },
  analysis: { headline: 'Breaking out of its base', summary: 's', evidence: [], invalidation: [], score: '87', copySource: 'template' },
  createdAt: '2026-11-09T19:32:05Z', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), decidedAt: null, rejectReason: null, assessment: ASSESSMENT(), ...over,
});

const ok = <T,>(data: T) => ({ data });

async function renderProposal() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  return renderRouter(
    {
      _layout: () => (
        <QueryClientProvider client={queryClient}>
          <Slot />
        </QueryClientProvider>
      ),
      'proposal/[id]': ProposalScreen,
      'stock/[ticker]': () => <Text>Stock screen</Text>,
      discover: () => <Text>Discover screen</Text>,
    },
    { initialUrl: '/proposal/p1' },
  );
}

beforeEach(() => {
  jest.clearAllMocks();
  api.get.mockResolvedValue(ok(PROPOSAL()));
  api.riskLimits.mockResolvedValue(ok({ maxRiskPerTradePct: '0.0075' }));
});

describe('Trade proposal (design 06)', () => {
  it('shows the research banner, levels, R:R, sizing, impact and the six design checks', async () => {
    await renderProposal();

    expect(await screen.findByText('RESEARCH PROPOSAL · NOT EXECUTED')).toBeOnTheScreen();
    expect(screen.getByText(/Nothing is sent until you hold to approve\. Paper account · expires/)).toBeOnTheScreen();
    expect(screen.getByText('BUY · LONG')).toBeOnTheScreen();
    expect(screen.getByText('BUY STOP · DAY')).toBeOnTheScreen();
    expect(screen.getByText('Base Breakout v3 · signal 14:32 ET ›')).toBeOnTheScreen();
    expect(screen.getByLabelText('Stop loss −4.45%: 178.20')).toBeOnTheScreen();
    expect(screen.getByLabelText('Target +9.38%: 204.00')).toBeOnTheScreen();
    expect(screen.getByLabelText('Loss at stop $630.80, gain at target $1,330.00, risk to reward 1 to 2.11')).toBeOnTheScreen();
    expect(screen.getByText('Sized by the max position weight')).toBeOnTheScreen();
    expect(screen.getByLabelText('Capital required: $14,174.00')).toBeOnTheScreen();
    expect(screen.getByLabelText('Cash after: $7,665.03')).toBeOnTheScreen();
    expect(screen.getByText('Weight 11.99%, close to 12% cap')).toBeOnTheScreen();
    expect(screen.getByText('Regular session open')).toBeOnTheScreen();
    expect(screen.queryByText('Stop 1.50× ATR')).not.toBeOnTheScreen(); // engine checks show only when they warn or fail
    expect(screen.getByText('Choices above your 0.75% limit are disabled.')).toBeOnTheScreen();
    await waitFor(() => expect(screen.getByTestId('proposal-approve')).toBeEnabled());
  });

  it('re-assesses on the server 150 ms after a change, disabling Approve meanwhile, and shows a failing reason', async () => {
    let resolve!: (v: unknown) => void;
    api.assess.mockReturnValue(new Promise((r) => (resolve = r)));
    await renderProposal();
    await screen.findByText('BUY STOP · DAY');

    await fireEvent.press(screen.getByRole('button', { name: /increase/i }));

    expect(screen.getByTestId('proposal-approve')).toBeDisabled();
    expect(screen.getByTestId('approve-disabled-reason')).toHaveTextContent('Checking the latest numbers…');
    await waitFor(() => expect(api.assess).toHaveBeenCalledWith('p1', { riskPerTradePct: '0.0075', quantity: '77' }, expect.anything()));
    resolve(ok({ ...ASSESSMENT(), quantity: '77', sizedBy: 'user', approvable: false, checks: [check('position_weight', 'fail', 'Position weight 12.15% exceeds 12% cap. Max 76 shares.')] }));

    await waitFor(() => expect(screen.getByTestId('approve-disabled-reason')).toHaveTextContent('Position weight 12.15% exceeds 12% cap. Max 76 shares.'));
    expect(screen.getByTestId('proposal-approve')).toBeDisabled();
  });

  it('cancels a recompute still in flight when the inputs change again', async () => {
    const signals: AbortSignal[] = [];
    api.assess.mockImplementation((_id: string, input: { quantity?: string }, signal: AbortSignal) => {
      signals.push(signal);
      return new Promise((r) => setTimeout(() => r(ok({ ...ASSESSMENT(), quantity: input.quantity ?? '76', sizedBy: 'user' })), 300));
    });
    await renderProposal();
    await screen.findByText('BUY STOP · DAY');

    await fireEvent.press(screen.getByRole('button', { name: /increase/i }));
    await waitFor(() => expect(api.assess).toHaveBeenCalledTimes(1));
    await fireEvent.press(screen.getByRole('button', { name: /decrease/i }));

    await waitFor(() => expect(api.assess).toHaveBeenCalledTimes(2));
    expect(signals[0]!.aborted).toBe(true);
    await waitFor(() => expect(screen.getByTestId('proposal-approve')).toBeEnabled(), { timeout: 2000 });
  });

  it('disables Approve once the assessment is a minute old and offers a recheck', async () => {
    api.get.mockResolvedValue(ok(PROPOSAL({ assessment: { ...ASSESSMENT(), expiresAt: new Date(Date.now() - 1000).toISOString() } })));
    api.assess.mockResolvedValue(ok(ASSESSMENT()));
    await renderProposal();

    expect(await screen.findByText('The checks are more than a minute old.')).toBeOnTheScreen();
    expect(screen.getByTestId('proposal-approve')).toBeDisabled();
    await fireEvent.press(screen.getByRole('button', { name: 'Recheck' }));

    await waitFor(() => expect(api.assess).toHaveBeenCalledWith('p1', { riskPerTradePct: '0.0075' }, expect.anything()));
    await waitFor(() => expect(screen.getByTestId('proposal-approve')).toBeEnabled());
  });

  it('rejects through the dialog and then hides the actions', async () => {
    api.reject.mockResolvedValue(ok(PROPOSAL({ status: 'rejected', rejectReason: 'user_rejected' })));
    await renderProposal();
    await screen.findByText('BUY STOP · DAY');

    await fireEvent.press(screen.getByTestId('proposal-reject'));
    expect(screen.getByText('No order is created. The signal stays in history so hit rates stay auditable.')).toBeOnTheScreen();
    await fireEvent.press(screen.getAllByRole('button', { name: 'Reject' }).at(-1)!); // the dialog's confirm

    expect(await screen.findByText('REJECTED · NO ORDER CREATED')).toBeOnTheScreen();
    expect(api.reject).toHaveBeenCalledWith('p1', undefined);
    expect(screen.queryByTestId('proposal-approve')).not.toBeOnTheScreen();
  });

  it('shows not found with a way back to Discover', async () => {
    api.get.mockRejectedValue(new ApiError({ status: 404, code: 'not_found' }));
    await renderProposal();

    expect(await screen.findByText('Proposal not found')).toBeOnTheScreen();
    await fireEvent.press(screen.getByRole('button', { name: 'Back to Discover' }));
    await waitFor(() => expect(screen).toHavePathname('/discover'));
  });
});
