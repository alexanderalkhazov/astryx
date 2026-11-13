import { type Proposal, type RiskAssessment, RISK_CHOICES } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import Decimal from 'decimal.js-light';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { CheckRow } from '@/components/CheckRow';
import { Dialog } from '@/components/Dialog';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { MetricGrid } from '@/components/Metric';
import { StackHeader } from '@/components/Navigation';
import { RewardRiskBar } from '@/components/RewardRiskBar';
import { Segmented } from '@/components/Segmented';
import { Screen } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Stepper } from '@/components/Stepper';
import { Text } from '@/components/Text';
import { approveGate, isNotFound, useNow, useProposal, useRejectProposal, useRiskLimits, useSizing } from '@/features/proposal/useProposal';
import { formatEtTime, formatMoney, formatPct, formatPrice } from '@/lib/format';

const ORDER_LABEL: Record<Proposal['orderType'], string> = { buy_stop: 'BUY STOP', limit: 'LIMIT', market: 'MARKET' };
const SIZED_BY: Record<RiskAssessment['sizedBy'], (a: RiskAssessment) => string> = {
  risk: (a) => `Sized by ${formatPct(a.riskPerTradePct)} risk per trade`,
  weight_cap: () => 'Sized by the max position weight',
  cash: () => 'Sized by available cash',
  user: () => 'Size set by you',
};
const DESIGN_CHECKS: Record<string, string> = {
  risk_per_trade: 'Risk per trade',
  position_weight: 'Position weight',
  sector_exposure: 'Sector exposure',
  cash_available: 'Cash available',
  daily_loss_halt: 'Daily loss halt',
  session_open: 'Regular session open',
};

// Design 06 · SYSTEM_DESIGN.md §8.4.4: the only way from research to an order. Every number comes from the server.
export default function ProposalScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const proposal = useProposal(id);
  const limits = useRiskLimits();
  const sizing = useSizing(proposal.data);
  const reject = useRejectProposal(id);
  const now = useNow();
  const [rejectOpen, setRejectOpen] = useState(false);
  const [, setConfirmOpen] = useState(false); // the confirm sheet arrives with T-088

  const header = <StackHeader title={proposal.data?.symbol ?? 'Proposal'} subtitle="Trade proposal" />;

  if (proposal.isError) {
    return (
      <Screen header={header}>
        {isNotFound(proposal.error) ? (
          <EmptyState title="Proposal not found" body="It may have been for another account, or it no longer exists." action={{ label: 'Back to Discover', onPress: () => router.replace('/discover') }} />
        ) : (
          <EmptyState title="Couldn't load the proposal" body="Check your connection and try again." action={{ label: 'Retry', onPress: () => void proposal.refetch() }} />
        )}
      </Screen>
    );
  }

  if (!proposal.data) {
    return (
      <Screen header={header}>
        <View testID="proposal-loading" style={styles.gap}>
          {[0, 1, 2, 3].map((i) => (
            <SkeletonRow key={i} index={i} />
          ))}
        </View>
      </Screen>
    );
  }

  const p = proposal.data;
  const a = sizing.current;
  const pending = p.status === 'pending' && Date.parse(p.expiresAt) > now;
  const gate = approveGate({ proposal: p, current: a, inputs: sizing.inputs, assessing: sizing.assessing, error: sizing.error, now });
  const dim = sizing.assessing ? styles.dim : undefined;
  const maxRisk = limits.data?.maxRiskPerTradePct;
  const quantity = sizing.inputs?.quantity ?? a?.quantity ?? '1';

  const footer = pending ? (
    <View style={styles.footer}>
      {!gate.enabled ? (
        <View style={styles.reason}>
          <Text.Caption tone="secondary" testID="approve-disabled-reason" style={styles.flex}>
            {gate.reason}
          </Text.Caption>
          {gate.recheck ? <Button variant="link" label="Recheck" onPress={() => sizing.recheck()} /> : null}
        </View>
      ) : null}
      <View style={styles.buttons}>
        <Button variant="secondary" label="Reject" block={false} onPress={() => setRejectOpen(true)} style={styles.reject} testID="proposal-reject" />
        <Button label="Approve simulated trade" block={false} disabled={!gate.enabled} onPress={() => setConfirmOpen(true)} style={styles.flex} testID="proposal-approve" />
      </View>
    </View>
  ) : undefined;

  return (
    <>
      <Screen header={header} footer={footer}>
        {pending ? (
          <StatusBanner tone="pending" title={p.label} body={`Nothing is sent until you hold to approve. Paper account · expires ${formatEtTime(p.expiresAt)}`} testID="proposal-banner" />
        ) : (
          <StatusBanner tone="rejected" title={p.status === 'approved' ? 'APPROVED' : p.status === 'rejected' ? 'REJECTED · NO ORDER CREATED' : 'EXPIRED · NO ORDER CREATED'} testID="proposal-banner" />
        )}

        <View style={styles.instrument}>
          <View style={styles.flex}>
            <Text.Title>{p.symbol}</Text.Title>
            <Text.Caption tone="secondary">{p.name}</Text.Caption>
          </View>
          <View style={styles.right}>
            <Badge label="BUY · LONG" tone="signal" mono />
            {a ? <Text.Data style={dim}>{formatPrice(a.quote.last)}</Text.Data> : null}
          </View>
        </View>

        <Pressable
          accessibilityRole="link"
          onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: p.symbol } })}
          style={({ pressed }) => [styles.source, { opacity: pressed ? 0.7 : 1 }]}
        >
          <Text.Caption tone="secondary">
            {p.source.type === 'signal'
              ? `${p.source.strategyName ?? 'Scanner'} · signal ${p.source.signalAt ? formatEtTime(p.source.signalAt) : ''} ›`
              : `AI analysis · ${p.source.signalAt ? formatEtTime(p.source.signalAt) : ''} ›`}
          </Text.Caption>
        </Pressable>

        <Section title="Order">
          <Text.Data>{`${ORDER_LABEL[p.orderType]} · DAY`}</Text.Data>
          <MetricGrid
            items={[
              { label: 'Entry', value: formatPrice(p.entry) },
              { label: `Stop loss ${formatPct(p.stopPct, { signed: true })}`, value: formatPrice(p.stop), tone: 'loss' },
              { label: `Target ${formatPct(p.targetPct, { signed: true })}`, value: formatPrice(p.target), tone: 'gain' },
            ]}
          />
          {a ? (
            <View style={dim}>
              <RewardRiskBar maxLoss={a.maxLoss} potentialProfit={a.potentialProfit} rewardRisk={a.rewardRisk} />
            </View>
          ) : null}
        </Section>

        <Section title="Position size">
          <Stepper value={quantity} min="1" unit="shares" onChange={(q) => sizing.setQuantity(q)} disabled={!pending || !a} />
          <Text.Caption tone="secondary">Risk per trade</Text.Caption>
          <Segmented
            accessibilityLabel="Risk per trade"
            options={RISK_CHOICES.map((value) => ({ value, label: formatPct(value), disabled: maxRisk !== undefined && new Decimal(value).gt(maxRisk) }))}
            value={(RISK_CHOICES.find((c) => sizing.inputs && new Decimal(c).eq(sizing.inputs.riskPerTradePct)) ?? RISK_CHOICES[1])}
            onChange={(r) => sizing.setRisk(r)}
          />
          {maxRisk !== undefined && RISK_CHOICES.some((c) => new Decimal(c).gt(maxRisk)) ? <Text.Caption tone="tertiary">{`Choices above your ${formatPct(maxRisk)} limit are disabled.`}</Text.Caption> : null}
          {a ? (
            <View style={[styles.gap, dim]}>
              <Text.Caption tone="secondary">{SIZED_BY[a.sizedBy](a)}</Text.Caption>
              <MetricGrid
                items={[
                  { label: 'Capital required', value: formatMoney(a.capitalRequired) },
                  { label: 'Max loss', value: `−${formatMoney(a.maxLoss)}`, tone: 'loss' },
                  { label: 'Potential profit', value: `+${formatMoney(a.potentialProfit)}`, tone: 'gain' },
                ]}
              />
            </View>
          ) : null}
        </Section>

        {a ? (
          <>
            <Section title="Fees and spread">
              <Row label="Commission" value={formatMoney(a.estFees)} dim={!!dim} />
              <Row label="Estimated slippage" value={formatMoney(a.estSlippage)} dim={!!dim} />
              <Row label="Max loss incl. costs" value={`−${formatMoney(a.maxLossInclCosts)}`} dim={!!dim} />
            </Section>

            <Section title="Portfolio impact">
              <Row label="New position weight" value={formatPct(a.impact.newWeightPct)} dim={!!dim} />
              <Row label={`${a.impact.sector ?? 'Sector'} exposure after`} value={formatPct(a.impact.sectorExposureAfterPct)} dim={!!dim} />
              <Row label="Cash after" value={formatMoney(a.impact.cashAfter)} dim={!!dim} />
            </Section>
          </>
        ) : null}

        <Section title="Pre-trade checks">
          <View style={styles.checks} testID="proposal-checks">
            {a && !sizing.error
              ? a.checks
                  .filter((c) => c.code in DESIGN_CHECKS || c.status !== 'pass') // engine checks show only when they warn or fail
                  .map((c, i) => <CheckRow key={c.code} first={i === 0} status={sizing.assessing ? 'pending' : c.status} label={DESIGN_CHECKS[c.code] ?? c.message} detail={c.code in DESIGN_CHECKS ? c.message : undefined} />)
              : sizing.error
                ? <CheckRow first status="fail" label="Couldn't verify — approval disabled" detail="Tap Recheck to try again." />
                : Object.values(DESIGN_CHECKS).map((label, i) => <CheckRow key={label} first={i === 0} status="pending" label={label} />)}
          </View>
        </Section>
      </Screen>

      <Dialog
        visible={rejectOpen}
        title="Reject this proposal?"
        body="No order is created. The signal stays in history so hit rates stay auditable."
        cancelLabel="Keep"
        confirmLabel="Reject"
        destructive
        busy={reject.isPending}
        onCancel={() => setRejectOpen(false)}
        onConfirm={() => reject.mutate(undefined, { onSettled: () => setRejectOpen(false) })}
      />
    </>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <Text.Overline tone="tertiary">{title}</Text.Overline>
      {children}
    </View>
  );
}

function Row({ label, value, dim }: { label: string; value: string; dim: boolean }) {
  return (
    <View style={styles.row} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text.Body tone="secondary">{label}</Text.Body>
      <Text.Data style={dim ? styles.dim : undefined}>{value}</Text.Data>
    </View>
  );
}

const styles = StyleSheet.create({
  gap: { gap: space[2] },
  flex: { flex: 1 },
  dim: { opacity: 0.45 },
  instrument: { flexDirection: 'row', alignItems: 'center', gap: space[3] },
  right: { alignItems: 'flex-end', gap: space[1] },
  source: { minHeight: 32, justifyContent: 'center' },
  section: { gap: space[3] },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', minHeight: 32 },
  checks: { borderWidth: 1, borderColor: color.line, borderRadius: radius.card, overflow: 'hidden' },
  footer: { gap: space[2] },
  reason: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
  buttons: { flexDirection: 'row', gap: space[3] },
  reject: { paddingHorizontal: space[5] },
});
