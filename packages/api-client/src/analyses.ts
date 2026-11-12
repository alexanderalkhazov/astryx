import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

type Schemas = components['schemas'];

const Decimal = z.string().regex(/^-?\d+(\.\d+)?$/, 'Expected a decimal string');

export const AnalysisJobStatus = z.enum(['pending', 'processing', 'completed', 'failed', 'cancelled']);
export type AnalysisJobStatus = z.infer<typeof AnalysisJobStatus>;

/** AI2 (SYSTEM_DESIGN.md §11.4). Poll at `pollAfterMs` while it is set; `resultId` arrives with `completed`. */
export const AnalysisJob = z.object({
  id: z.string(),
  symbol: z.string(),
  kind: z.string(),
  status: AnalysisJobStatus,
  attempt: z.number().int(),
  createdAt: z.string(),
  startedAt: z.string().nullable(),
  completedAt: z.string().nullable(),
  resultId: z.string().nullable(),
  errorCode: z.string().nullable(),
  errorDetail: z.string().nullable(),
  pollAfterMs: z.number().int().nullable(),
}) satisfies z.ZodType<Omit<Schemas['AnalysisJobDto'], 'status'> & { status: string }>;
export type AnalysisJob = z.infer<typeof AnalysisJob>;

const Sentiment = z.enum(['bullish', 'bearish', 'neutral', 'mixed']);
const Scenario = z.object({ summary: z.string(), conditions: z.array(z.string()) });

/** AI4: the validated analysis.v1 (§12.6) plus the backend's fields. `confidence.value` is NOT a calibrated probability. */
export const AnalysisResult = z.object({
  id: z.string(),
  jobId: z.string(),
  schemaVersion: z.string(),
  symbol: z.string(),
  analysisTimestamp: z.string(),
  dataTimestamp: z.string(),
  dataQuality: z.object({ status: z.string(), notes: z.array(z.string()).nullable() }),
  overallSentiment: Sentiment,
  scenarios: z.object({ bullish: Scenario, bearish: Scenario, neutral: Scenario }),
  supportingEvidence: z.array(z.string()),
  contradictingEvidence: z.array(z.string()),
  keyRisks: z.array(z.string()),
  relevantIndicators: z.array(z.object({ name: z.string(), value: z.string(), interpretation: z.string() })),
  catalysts: z.array(z.object({ type: z.string(), date: z.string().nullable(), note: z.string() })),
  confidence: z.object({ value: Decimal, explanation: z.string() }),
  suggestedLevels: z.object({
    justified: z.boolean(),
    entryZone: z.object({ low: Decimal.nullable(), high: Decimal.nullable() }).nullable(),
    stopLoss: Decimal.nullable(),
    takeProfit: Decimal.nullable(),
    rationale: z.string().nullable(),
    accepted: z.boolean(),
  }),
  explanation: z.string(),
  insufficientData: z.boolean(),
  /** Computed by the backend from the levels, never by the model. */
  rewardRisk: Decimal.nullable(),
  levelValidation: z.object({ accepted: z.boolean(), reasons: z.array(z.string()) }),
  stale: z.boolean(),
  model: z.string(),
  promptVersion: z.string(),
  disclaimer: z.string(),
  validationStatus: z.enum(['valid', 'valid_with_rejections']),
});
export type AnalysisResult = z.infer<typeof AnalysisResult>;

export const AnalysisSummary = z.object({
  id: z.string(),
  jobId: z.string(),
  symbol: z.string(),
  overallSentiment: Sentiment.nullable(),
  confidence: Decimal.nullable(),
  analysisAt: z.string(),
  dataAsOf: z.string(),
  stale: z.boolean(),
  levelsAccepted: z.boolean(),
  rewardRisk: Decimal.nullable(),
});
export type AnalysisSummary = z.infer<typeof AnalysisSummary>;

export const AnalysisPage = z.object({ items: z.array(AnalysisSummary), nextCursor: z.string().nullable() });
export type AnalysisPage = z.infer<typeof AnalysisPage>;

/** AI analyses AI1–AI5. `create` answers 202 (new job) or 200 (the equivalent existing job). */
export function analysesApi(client: ApiClient) {
  return {
    create: (symbol: string, idempotencyKey: string) =>
      client.request('/api/v1/analyses', { method: 'POST', body: { symbol }, idempotencyKey, schema: AnalysisJob }),
    job: (jobId: string) => client.request(`/api/v1/analyses/jobs/${encodeURIComponent(jobId)}`, { schema: AnalysisJob }),
    cancel: (jobId: string) => client.request(`/api/v1/analyses/jobs/${encodeURIComponent(jobId)}`, { method: 'DELETE', schema: AnalysisJob }),
    get: (id: string) => client.request(`/api/v1/analyses/${encodeURIComponent(id)}`, { schema: AnalysisResult }),
    list: (params: { symbol?: string; limit?: number; cursor?: string } = {}) => {
      const entries = Object.entries(params).filter((e): e is [string, string | number] => e[1] !== undefined);
      const query = entries.length ? `?${new URLSearchParams(entries.map(([k, v]) => [k, String(v)])).toString()}` : '';
      return client.request(`/api/v1/analyses${query}`, { schema: AnalysisPage });
    },
  };
}
