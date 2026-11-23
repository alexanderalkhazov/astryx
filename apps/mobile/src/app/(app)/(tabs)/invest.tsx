import { type ContributionFrequency, type InvestPlan, type InvestProfile, isApiError } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { randomUUID } from 'expo-crypto';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { SkeletonRow } from '@/components/Feedback';
import { Input } from '@/components/Input';
import { MetricHero } from '@/components/Metric';
import { Segmented } from '@/components/Segmented';
import { PaperChip, Screen, ScreenHeader } from '@/components/Shell';
import { Text } from '@/components/Text';
import { type LossTolerance, useInvestorProfile } from '@/features/onboarding/profile';
import { WhatIf } from '@/features/plain/WhatIf';
import { INTRO } from '@/features/plain/screenIntros';
import { invest } from '@/lib/api';
import { formatMoney, formatPct, formatPnl, formatSessionDate } from '@/lib/format';
import { useSession } from '@/state/session';
import { toast } from '@/state/toasts';

export const investKey = ['invest'] as const;

const PROFILES: Record<InvestProfile, { label: string; mix: string; about: string }> = {
  cautious: { label: 'Cautious', mix: '40% stocks · 60% bonds', about: 'Smaller ups and downs; grows slowly.' },
  balanced: { label: 'Balanced', mix: '70% stocks · 30% bonds', about: 'A middle path between growth and calm.' },
  growth: { label: 'Growth', mix: '90% stocks · 10% bonds', about: 'Bigger swings; more growth over many years.' },
};

/** The onboarding answer about a 10 % drop suggests the starting mix (T-162). */
const SUGGESTED: Record<LossTolerance, InvestProfile> = { sell: 'cautious', hold: 'balanced', normal: 'growth' };

const FREQUENCY_LABEL: Record<ContributionFrequency, string> = { none: 'None', weekly: 'Weekly', monthly: 'Monthly' };

// T-164: the long-term investing plan, a paper portfolio of three broad index funds, separate from trading.
export default function InvestScreen() {
  const plan = useQuery({
    queryKey: investKey,
    queryFn: async () => {
      try {
        return (await invest.get()).data;
      } catch (error) {
        if (isApiError(error) && error.status === 404) return null; // no plan yet: show the setup
        throw error;
      }
    },
  });
  const [editing, setEditing] = useState(false);

  return (
    <Screen intro={INTRO.invest} header={<ScreenHeader title="Invest" chip={<PaperChip />} />}>
      {plan.isPending ? (
        <SkeletonRow />
      ) : plan.data === null || editing ? (
        <PlanForm existing={plan.data ?? null} onDone={() => setEditing(false)} />
      ) : plan.data ? (
        <PlanView plan={plan.data} onEdit={() => setEditing(true)} />
      ) : (
        <Text.Body tone="secondary">{"Couldn't load your plan. Pull to try again."}</Text.Body>
      )}
      <Text.Caption tone="tertiary">
        Paper money. Funds are bought at the latest price without fees. Past results of these funds don&apos;t guarantee future returns, and
        this isn&apos;t investment advice.
      </Text.Caption>
    </Screen>
  );
}

function useSaved() {
  const queryClient = useQueryClient();
  return (data: InvestPlan) => queryClient.setQueryData(investKey, data);
}

function PlanForm({ existing, onDone }: { existing: InvestPlan | null; onDone: () => void }) {
  const tolerance = useInvestorProfile(useSession((s) => s.user?.id))?.tolerance;
  const [picked, setProfile] = useState<InvestProfile | undefined>(existing?.profile);
  // The onboarding answers load after the first render: suggest from them until the user picks.
  const profile: InvestProfile = picked ?? (tolerance ? SUGGESTED[tolerance] : 'balanced');
  const [deposit, setDeposit] = useState('10000');
  const [frequency, setFrequency] = useState<ContributionFrequency>(existing?.contribution.frequency ?? 'monthly');
  const [amount, setAmount] = useState(
    existing && existing.contribution.frequency !== 'none' ? new Decimal(existing.contribution.amount).toFixed(0) : '200',
  );
  const [goal, setGoal] = useState(existing?.goal ? new Decimal(existing.goal.amount).toFixed(0) : '');
  const saved = useSaved();
  const save = useMutation({
    mutationFn: async () =>
      (
        await invest.save(
          {
            profile,
            initialDeposit: existing ? undefined : deposit,
            contributionAmount: frequency === 'none' ? '0' : amount,
            contributionFrequency: frequency,
            goalAmount: goal.trim() ? goal.trim() : null,
          },
          randomUUID(),
        )
      ).data,
    onSuccess: (data) => {
      saved(data);
      toast({
        tone: 'success',
        title: existing ? 'Plan updated' : 'Your plan is invested',
        body: existing ? undefined : 'Paper money, spread across three funds.',
      });
      onDone();
    },
    onError: (error) =>
      toast({
        tone: 'error',
        title: "Couldn't save the plan",
        body: isApiError(error) ? (error.detail ?? error.message) : 'Check your connection and try again.',
      }),
  });

  return (
    <View style={styles.content} testID="invest-form">
      {!existing ? (
        <>
          <Text.Title accessibilityRole="header">Invest for the long term</Text.Title>
          <Text.Body tone="secondary">
            Spread money across three broad funds (US stocks, international stocks and bonds) and add to it regularly. It&apos;s the
            simplest way most people build wealth, and it doesn&apos;t need you to pick stocks.
          </Text.Body>
        </>
      ) : null}
      <View style={styles.group} accessibilityRole="radiogroup" accessibilityLabel="Mix">
        <Text.Section>Mix</Text.Section>
        {(Object.keys(PROFILES) as InvestProfile[]).map((key) => {
          const selected = profile === key;
          return (
            <Pressable
              key={key}
              accessibilityRole="radio"
              accessibilityState={{ selected }}
              accessibilityLabel={`${PROFILES[key].label}, ${PROFILES[key].mix}`}
              onPress={() => setProfile(key)}
              style={[styles.option, selected && styles.selected]}
              testID={`invest-profile-${key}`}
            >
              <Text.Body>{`${PROFILES[key].label} · ${PROFILES[key].mix}`}</Text.Body>
              <Text.Caption tone="secondary">{PROFILES[key].about}</Text.Caption>
            </Pressable>
          );
        })}
      </View>
      {!existing ? (
        <Input label="Starting amount ($)" value={deposit} onChangeText={setDeposit} keyboardType="number-pad" testID="invest-deposit" />
      ) : null}
      <View style={styles.group}>
        <Text.Section>Add regularly</Text.Section>
        <Segmented
          accessibilityLabel="Contribution frequency"
          mono={false}
          options={(Object.keys(FREQUENCY_LABEL) as ContributionFrequency[]).map((value) => ({ value, label: FREQUENCY_LABEL[value] }))}
          value={frequency}
          onChange={(value) => setFrequency(value as ContributionFrequency)}
        />
        {frequency !== 'none' ? (
          <Input label="Amount each time ($)" value={amount} onChangeText={setAmount} keyboardType="number-pad" testID="invest-amount" />
        ) : null}
      </View>
      <Input
        label="Goal (optional, $)"
        value={goal}
        onChangeText={setGoal}
        keyboardType="number-pad"
        placeholder="e.g. 50000"
        testID="invest-goal"
      />
      <Button
        label={existing ? 'Save changes' : 'Start investing'}
        onPress={() => save.mutate()}
        loading={save.isPending}
        testID="invest-save"
      />
      {existing ? <Button variant="link" label="Cancel" onPress={onDone} /> : null}
    </View>
  );
}

function PlanView({ plan, onEdit }: { plan: InvestPlan; onEdit: () => void }) {
  const saved = useSaved();
  const act = (fn: (key: string) => Promise<{ data: InvestPlan }>, done: string) => ({
    mutationFn: async () => (await fn(randomUUID())).data,
    onSuccess: (data: InvestPlan) => {
      saved(data);
      toast({ tone: 'success', title: done });
    },
    onError: (error: unknown) =>
      toast({
        tone: 'error',
        title: "Couldn't do that",
        body: isApiError(error) ? (error.detail ?? error.message) : 'Check your connection and try again.',
      }),
  });
  const rebalance = useMutation(act(invest.rebalance, 'Back to your target mix'));
  const investCash = useMutation(act(invest.investCash, 'Cash invested'));
  const cash = new Decimal(plan.cash);
  const tone = new Decimal(plan.gain).isNegative() ? 'loss' : 'gain';

  return (
    <View style={styles.content} testID="invest-plan">
      <MetricHero label={`Investing plan · ${PROFILES[plan.profile].label}`} value={plan.value}>
        <Text.DataSm tone={tone}>{`${formatPnl(plan.gain)} (${formatPct(plan.gainPct, { signed: true })}) since you started`}</Text.DataSm>
        <Text.Caption tone="tertiary">{`${formatMoney(plan.contributed)} put in so far`}</Text.Caption>
      </MetricHero>

      <View style={styles.group}>
        <Text.Section>Your mix</Text.Section>
        {plan.funds.map((fund) => (
          <View key={fund.symbol} style={styles.fund} testID={`invest-fund-${fund.symbol}`}>
            <View style={styles.fundHead}>
              <Text.Body style={styles.flex}>{`${fund.name} · ${fund.symbol}`}</Text.Body>
              <Text.Data>{formatMoney(fund.value)}</Text.Data>
            </View>
            <View
              style={styles.track}
              accessible
              accessibilityLabel={`${fund.name}: ${formatPct(fund.weight, { dp: 0 })} now, target ${formatPct(fund.targetWeight, { dp: 0 })}`}
            >
              <View style={[styles.fill, { width: `${Math.min(100, Number(new Decimal(fund.weight).times(100).toFixed(1)))}%` }]} />
              <View style={[styles.target, { left: `${Number(new Decimal(fund.targetWeight).times(100).toFixed(1))}%` }]} />
            </View>
            <Text.Caption tone="tertiary">{`${formatPct(fund.weight, { dp: 0 })} now · target ${formatPct(fund.targetWeight, { dp: 0 })}`}</Text.Caption>
          </View>
        ))}
        {plan.rebalanceSuggested ? (
          <View style={styles.callout} testID="invest-rebalance-suggested">
            <Text.Body tone="secondary">{`Your mix has drifted ${formatPct(plan.maxDrift, { dp: 0 })} from its target. Rebalancing sells a little of what grew and buys what lagged.`}</Text.Body>
            <Button
              variant="secondary"
              label="Rebalance"
              onPress={() => rebalance.mutate()}
              loading={rebalance.isPending}
              testID="invest-rebalance"
            />
            <WhatIf action="rebalance" />
          </View>
        ) : null}
        {cash.gte(1) ? (
          <View style={styles.callout}>
            <Text.Body tone="secondary">{`${formatMoney(plan.cash)} is waiting to be invested.`}</Text.Body>
            <Button
              variant="secondary"
              label="Invest it"
              onPress={() => investCash.mutate()}
              loading={investCash.isPending}
              testID="invest-cash"
            />
            <WhatIf action="invest" />
          </View>
        ) : null}
      </View>

      <View style={styles.group}>
        <Text.Section>Adding regularly</Text.Section>
        <Text.Body tone="secondary" testID="invest-contribution">
          {plan.contribution.frequency === 'none'
            ? 'No regular additions. Adding a little every month is how plans grow.'
            : `${formatMoney(plan.contribution.amount)} ${plan.contribution.frequency === 'weekly' ? 'every week' : 'every month'}${plan.contribution.nextOn ? ` · next on ${formatSessionDate(plan.contribution.nextOn)}` : ''}`}
        </Text.Body>
      </View>

      {plan.goal ? (
        <View style={styles.group} testID="invest-goal-progress">
          <Text.Section>{`Goal · ${formatMoney(plan.goal.amount)}`}</Text.Section>
          <View style={styles.track}>
            <View style={[styles.fill, { width: `${Number(new Decimal(plan.goal.progress).times(100).toFixed(1))}%` }]} />
          </View>
          <Text.Caption tone="tertiary">{`${formatPct(plan.goal.progress, { dp: 0 })} of the way there`}</Text.Caption>
        </View>
      ) : null}

      {plan.activity.length > 0 ? (
        <View style={styles.group}>
          <Text.Section>Recent activity</Text.Section>
          {plan.activity.slice(0, 8).map((a, i) => (
            <View key={`${a.occurredAt}-${i}`} style={styles.fundHead}>
              <Text.Caption tone="secondary" style={styles.flex}>
                {a.kind === 'deposit' ? 'Added money' : `${a.kind === 'buy' ? 'Bought' : 'Sold'} ${a.symbol}`}
              </Text.Caption>
              <Text.DataSm>{formatMoney(new Decimal(a.amount).abs().toString())}</Text.DataSm>
            </View>
          ))}
        </View>
      ) : null}

      <Button variant="secondary" label="Change plan" onPress={onEdit} testID="invest-edit" />
    </View>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[5], paddingTop: space[3] },
  group: { gap: space[2] },
  option: {
    gap: space[1],
    padding: space[4],
    borderRadius: radius.card,
    borderWidth: 1,
    borderColor: color.line,
    backgroundColor: color.surface1,
  },
  selected: { borderColor: color.signal },
  fund: { gap: space[1] },
  fundHead: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
  flex: { flex: 1 },
  track: { height: 8, borderRadius: 4, backgroundColor: color.surface2, overflow: 'visible' },
  fill: { height: 8, borderRadius: 4, backgroundColor: color.signal },
  target: { position: 'absolute', top: -3, width: 2, height: 14, backgroundColor: color.text2 },
  callout: {
    gap: space[2],
    padding: space[3],
    borderRadius: radius.card,
    backgroundColor: color.surface1,
    borderWidth: 1,
    borderColor: color.line,
  },
});
