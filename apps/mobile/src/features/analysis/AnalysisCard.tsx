import { type AnalysisJob, type AnalysisResult, isApiError } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { Sparkles } from 'lucide-react-native';
import { useEffect } from 'react';
import { StyleSheet, View } from 'react-native';

import { Badge, type BadgeTone } from '@/components/Badge';
import { Button } from '@/components/Button';
import { SkeletonRow, Spinner } from '@/components/Feedback';
import { MetricGrid } from '@/components/Metric';
import { Text } from '@/components/Text';
import { Bullet, Section } from '@/features/stock/Sections';
import { formatEtTime, formatPrice, formatRatio, formatSessionDate } from '@/lib/format';

import { analysisKeys } from './keys';
import { isTerminal, useAnalysisJob, useAnalysisJobs, useCancelAnalysis, useLatestAnalysis, useRequestAnalysis } from './useAnalysis';

export const ANALYSIS_FAILED = "Analysis couldn't be generated reliably. Indicators are still available.";
export const CONFIDENCE_NOTE = 'A subjective model rating of how well the evidence lines up. Not a calibrated probability.';

const SENTIMENT: Record<AnalysisResult['overallSentiment'], { label: string; tone: BadgeTone }> = {
  bullish: { label: 'Bullish', tone: 'signal' },
  bearish: { label: 'Bearish', tone: 'pink' },
  neutral: { label: 'Neutral', tone: 'neutral' },
  mixed: { label: 'Mixed', tone: 'violet' },
};

const when = (instant: string) => `${formatSessionDate(instant.slice(0, 10))} · ${formatEtTime(instant)}`;

/**
 * "Analyze with AI" in Stock › Signal (SYSTEM_DESIGN.md §8.4.3, §12, T-075): request a job, follow its progress
 * (SignalR with polling fallback), then render the validated analysis. Research only; labelled as an experimental
 * model whose confidence is not a probability.
 */
export function AnalysisCard({ symbol }: { symbol: string }) {
  const queryClient = useQueryClient();
  const latest = useLatestAnalysis(symbol);
  const jobId = useAnalysisJobs((s) => s.bySymbol[symbol]);
  const job = useAnalysisJob(jobId);
  const request = useRequestAnalysis(symbol);
  const cancel = useCancelAnalysis();

  const current = job.data;
  const finished = current?.status === 'completed';
  useEffect(() => {
    // The socket refreshes the list too; this covers the polling path.
    if (finished) void queryClient.invalidateQueries({ queryKey: analysisKeys.list(symbol) });
  }, [finished, queryClient, symbol]);

  const running = current !== undefined && !isTerminal(current);
  const failed = current?.status === 'failed' && latest.data?.jobId !== current.id;
  const result = latest.data;
  const requestError = request.error ? requestErrorText(request.error) : null;

  return (
    <View style={styles.card} testID="analysis-card">
      <View style={styles.header}>
        <View style={styles.title}>
          <Sparkles size={16} strokeWidth={1.7} color={color.pending} />
          <Text.Section accessibilityRole="header">AI analysis</Text.Section>
        </View>
        <Badge label="Experimental AI model" tone="pending" outlined />
      </View>

      {running ? (
        <Progress job={current} onCancel={() => cancel.mutate(current.id)} cancelling={cancel.isPending || (current.status === 'processing' && cancel.isSuccess)} />
      ) : failed ? (
        <View style={styles.block} testID="analysis-failed">
          <Text.Body>{current.errorCode === 'ai_output_invalid' || !current.errorDetail ? ANALYSIS_FAILED : current.errorDetail}</Text.Body>
          <Button variant="secondary" label="Try again" onPress={() => request.mutate()} loading={request.isPending} />
        </View>
      ) : null}

      {!running && latest.isPending ? <SkeletonRow /> : null}

      {!running && !latest.isPending && !result && !failed ? (
        <View style={styles.block}>
          <Text.Body tone="secondary">
            Scenarios, evidence for and against, risks and catalysts, written by a local model from this stock's data. It usually takes a minute or two.
          </Text.Body>
          <Button label="Analyze with AI" onPress={() => request.mutate()} loading={request.isPending} testID="analyze-with-ai" />
        </View>
      ) : null}

      {requestError ? (
        <Text.Caption tone="loss" accessibilityLiveRegion="polite">
          {requestError}
        </Text.Caption>
      ) : null}

      {result ? <Result result={result} /> : null}

      {result && !running ? <Button variant="link" label="Run a new analysis" onPress={() => request.mutate()} disabled={request.isPending} /> : null}

      <Text.Caption tone="tertiary">
        {result?.disclaimer ?? 'AI-generated research for information only. Not investment advice. Not a guarantee of any outcome.'}
      </Text.Caption>
    </View>
  );
}

function requestErrorText(error: Error): string {
  if (!isApiError(error)) return 'Something went wrong. Try again.';
  if (error.isNetworkError) return "Couldn't reach Astryx. Check your connection and try again.";
  if (error.code === 'ai_quota_exceeded' || error.code === 'rate_limited') return error.detail ?? "You've used today's AI analyses.";
  return error.detail ?? 'The analysis could not be started.';
}

function Progress({ job, onCancel, cancelling }: { job: AnalysisJob; onCancel: () => void; cancelling: boolean }) {
  const label =
    job.status === 'processing'
      ? `Analyzing ${job.symbol}${job.attempt > 1 ? ` · attempt ${job.attempt} of 3` : ''}`
      : job.attempt > 0
        ? `Waiting to retry · attempt ${job.attempt + 1} of 3`
        : 'Queued';
  return (
    <View style={styles.progress} testID="analysis-progress" accessible accessibilityLabel={`${label}. You can leave this screen; the analysis keeps running.`}>
      <Spinner label={label} />
      <View style={styles.progressText}>
        <Text.Body>{label}</Text.Body>
        <Text.Caption tone="tertiary">You can leave this screen; it keeps running.</Text.Caption>
      </View>
      <Button variant="link" label={cancelling ? 'Cancelling…' : 'Cancel'} onPress={onCancel} disabled={cancelling} />
    </View>
  );
}

function Result({ result }: { result: AnalysisResult }) {
  const sentiment = SENTIMENT[result.overallSentiment];
  const levels = result.suggestedLevels;
  return (
    <View style={styles.result} testID="analysis-result">
      <View style={styles.badges}>
        <Badge label={sentiment.label} tone={sentiment.tone} />
        {result.stale ? <Badge label="Stale" tone="pending" mono /> : null}
        {result.insufficientData ? <Badge label="Insufficient data" tone="neutral" /> : null}
      </View>
      <Text.Body>{result.explanation}</Text.Body>

      <Section title="Scenarios">
        {(['bullish', 'bearish', 'neutral'] as const).map((key) => (
          <View key={key} style={styles.scenario}>
            <Text.Section>{`${SENTIMENT[key].label} case`}</Text.Section>
            <Text.Body tone="secondary">{result.scenarios[key].summary}</Text.Body>
            {result.scenarios[key].conditions.map((c) => (
              <Bullet key={c} text={c} />
            ))}
          </View>
        ))}
      </Section>

      <List title="Supporting evidence" items={result.supportingEvidence} />
      <List title="Contradicting evidence" items={result.contradictingEvidence} />
      <List title="Key risks" items={result.keyRisks} />
      <List title="Catalysts" items={result.catalysts.map((c) => (c.date ? `${formatSessionDate(c.date)} · ${c.note}` : c.note))} />

      <Section title="Confidence">
        <Text.Data>{formatRatio(result.confidence.value, 2)}</Text.Data>
        <Text.Caption tone="secondary">{CONFIDENCE_NOTE}</Text.Caption>
        <Text.Body tone="secondary">{result.confidence.explanation}</Text.Body>
      </Section>

      <Section title="Suggested levels">
        {levels.justified && levels.entryZone?.low && levels.entryZone.high && levels.stopLoss && levels.takeProfit ? (
          <MetricGrid
            items={[
              { label: 'Entry zone', value: `${formatPrice(levels.entryZone.low)}–${formatPrice(levels.entryZone.high)}` },
              { label: 'Stop', value: formatPrice(levels.stopLoss), tone: 'loss' },
              { label: 'Target', value: formatPrice(levels.takeProfit), tone: 'gain' },
              ...(result.rewardRisk ? [{ label: 'R:R', value: formatRatio(result.rewardRisk) }] : []),
            ]}
          />
        ) : null}
        {result.levelValidation.accepted ? (
          <Text.Caption tone="secondary">Passed the deterministic level checks. A proposal would still go through the risk engine.</Text.Caption>
        ) : levels.justified ? (
          <View style={styles.block} testID="levels-rejected">
            <Text.Caption tone="secondary">Rejected by the deterministic level checks, so these levels can't seed a proposal:</Text.Caption>
            {result.levelValidation.reasons.map((r) => (
              <Bullet key={r} text={r} />
            ))}
          </View>
        ) : (
          <Text.Caption tone="secondary">The model didn't suggest levels for this data.</Text.Caption>
        )}
      </Section>

      <Text.Caption tone="tertiary">{`${result.model} · prompt ${result.promptVersion} · analysed ${when(result.analysisTimestamp)} from data as of ${formatEtTime(result.dataTimestamp)}`}</Text.Caption>
    </View>
  );
}

function List({ title, items }: { title: string; items: readonly string[] }) {
  if (items.length === 0) return null;
  return (
    <Section title={title}>
      {items.map((item) => (
        <Bullet key={item} text={item} />
      ))}
    </Section>
  );
}

const styles = StyleSheet.create({
  card: { gap: space[4], padding: space[4], borderRadius: radius.card, borderWidth: 1, borderStyle: 'dashed', borderColor: color.lineStrong },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: space[2], flexWrap: 'wrap' },
  title: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
  block: { gap: space[3] },
  progress: { flexDirection: 'row', alignItems: 'center', gap: space[3] },
  progressText: { flex: 1, gap: space[1] },
  result: { gap: space[4] },
  badges: { flexDirection: 'row', gap: space[2], flexWrap: 'wrap' },
  scenario: { gap: space[1] },
});
