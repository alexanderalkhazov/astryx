import type { Proposal, RiskAssessment } from '@astryx/api-client';

import { approveGate } from '../useProposal';

const NOW = Date.parse('2026-11-09T19:35:00Z');
const proposal = { status: 'pending', expiresAt: '2026-11-09T20:55:00Z' } as Proposal;
const assessment = (checks: { code: string; status: string; message: string }[], over: Partial<RiskAssessment> = {}) =>
  ({ riskPerTradePct: '0.0075', quantity: '76', checks, stale: false, expiresAt: '2026-11-09T19:36:00Z', approvable: checks.every((c) => c.status !== 'fail'), ...over }) as unknown as RiskAssessment;
const gate = (current: RiskAssessment) => approveGate({ proposal, current, inputs: { riskPerTradePct: '0.0075' }, assessing: false, error: null, now: NOW });

describe('approveGate', () => {
  it('enables approval when every check passes', () => {
    expect(gate(assessment([{ code: 'risk_per_trade', status: 'pass', message: 'ok' }]))).toEqual({ enabled: true });
  });

  it('offers Recheck when only stale data fails, since fresh prices can pass', () => {
    expect(gate(assessment([{ code: 'data_fresh', status: 'fail', message: 'Prices are stale (last 03:38 ET). Approvals disabled.' }]))).toEqual({
      enabled: false,
      reason: 'Prices are stale (last 03:38 ET). Approvals disabled.',
      recheck: true,
    });
  });

  it('needs a change of inputs, not a recheck, for other failing checks', () => {
    const result = gate(assessment([{ code: 'position_weight', status: 'fail', message: 'Position weight 14.0% exceeds 12% cap.' }]));
    expect(result).toEqual({ enabled: false, reason: 'Position weight 14.0% exceeds 12% cap.' });
  });

  it('offers Recheck once the checks are over a minute old', () => {
    expect(gate(assessment([{ code: 'risk_per_trade', status: 'pass', message: 'ok' }], { expiresAt: '2026-11-09T19:34:00Z' }))).toMatchObject({ enabled: false, recheck: true });
  });
});
