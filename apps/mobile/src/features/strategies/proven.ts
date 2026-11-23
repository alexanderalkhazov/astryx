import type { Signal } from '@astryx/api-client';

/** Setups a beginner may see: the strategy made money in testing and finished its live trial within its tested drawdown (T-166). */
export const provenForBeginners = (s: Signal) =>
  (s.strategy.evidence === 'beats_market' || s.strategy.evidence === 'lags_market') && s.strategy.soaked === true;
