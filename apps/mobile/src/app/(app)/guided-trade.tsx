import { isApiError, type PaperOrder } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { Text } from '@/components/Text';
import { type GuidedProgress, loadProgress, pickSetup, saveProgress, type Step, STEPS, stepCopy } from '@/features/guided/guidedTrade';
import { ConfirmOrderSheet } from '@/features/proposal/ConfirmOrderSheet';
import { approveGate, proposalKey, useNow, useProposal, useSizing } from '@/features/proposal/useProposal';
import { WhatIf } from '@/features/plain/WhatIf';
import { proposals, scanner } from '@/lib/api';
import { formatPrice } from '@/lib/format';
import { useSession } from '@/state/session';

const COUNTED = STEPS.filter((s) => s !== 'intro' && s !== 'done');
const header = <StackHeader title="Your first practice trade" subtitle="Pretend money" />;

// T-193: a step-by-step first trade on a real setup with practice money. Skippable; it resumes where you left off.
export default function GuidedTradeScreen() {
  const userId = useSession((s) => s.user?.id);
  const [progress, setProgressState] = useState<GuidedProgress>();
  const [starting, setStarting] = useState(false);
  const [problem, setProblem] = useState<string>();
  const [tried, setTried] = useState<string[]>([]);

  useEffect(() => {
    if (userId) void loadProgress(userId).then(setProgressState);
  }, [userId]);

  const go = (next: GuidedProgress) => {
    setProgressState(next);
    if (userId) void saveProgress(userId, next);
  };

  const start = async (exclude: string[] = tried) => {
    setStarting(true);
    setProblem(undefined);
    try {
      const signals = (await scanner.list({ sort: 'score' }, { limit: 20 })).data.items;
      const setup = pickSetup(signals, exclude);
      if (!setup) {
        setProblem('There are no trade ideas right now. Ideas appear while the market is open; try again then.');
        return;
      }
      setTried([...exclude, setup.signal.symbol]);
      const proposal = (await proposals.fromSignal(setup.signal.id)).data;
      go({ step: 'stock', proposalId: proposal.id });
    } catch (error) {
      setProblem(isApiError(error) ? (error.detail ?? error.message) : 'Check your connection and try again.');
    } finally {
      setStarting(false);
    }
  };

  const skip = () => router.replace('/');

  if (!progress) return <Screen header={header}>{null}</Screen>;

  if (progress.step === 'intro' || !progress.proposalId) {
    return (
      <Screen
        header={header}
        footer={
          <View style={styles.footer}>
            <Button label={problem ? 'Try again' : 'Start'} onPress={() => void start()} loading={starting} testID="guided-start" />
            <Button variant="link" label="Skip for now" onPress={skip} testID="guided-skip" />
          </View>
        }
      >
        <View style={styles.content} testID="guided-intro">
          <Text.Title accessibilityRole="header">Let’s make a practice trade together</Text.Title>
          <Text.Body tone="secondary">
            Five short steps, about two minutes. We’ll use a real idea from today’s market and pretend money, so nothing can go wrong.
          </Text.Body>
          {problem ? (
            <Text.Body tone="loss" testID="guided-problem">
              {problem}
            </Text.Body>
          ) : null}
        </View>
      </Screen>
    );
  }

  return (
    <ProposalSteps
      key={progress.proposalId}
      progress={progress as Required<GuidedProgress>}
      go={go}
      onSkip={skip}
      onNewIdea={() => void start()}
      starting={starting}
    />
  );
}

function ProposalSteps({
  progress,
  go,
  onSkip,
  onNewIdea,
  starting,
}: {
  progress: Required<GuidedProgress>;
  go: (next: GuidedProgress) => void;
  onSkip: () => void;
  onNewIdea: () => void;
  starting: boolean;
}) {
  const queryClient = useQueryClient();
  const proposal = useProposal(progress.proposalId);
  const sizing = useSizing(proposal.data);
  const now = useNow();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [placed, setPlaced] = useState<PaperOrder | null>(null);
  const { step } = progress;
  const to = (next: Step) => go({ ...progress, step: next });

  // The assessment is valid for a minute: check the numbers again when the user reaches the risk step.
  const { recheck } = sizing;
  const hasProposal = proposal.data !== undefined;
  useEffect(() => {
    if (hasProposal && (step === 'risk' || step === 'approve')) recheck();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step, hasProposal]);

  if (step === 'done') {
    const waiting = placed?.status !== 'filled';
    return (
      <Screen
        header={header}
        footer={
          <View style={styles.footer}>
            <Button label="Watch it in Activity" onPress={() => router.replace('/activity')} testID="guided-activity" />
            <Button variant="link" label="Back to Home" onPress={() => router.replace('/')} />
          </View>
        }
      >
        <View style={styles.content} testID="guided-done">
          <Text.Overline tone="signal">DONE</Text.Overline>
          <Text.Title accessibilityRole="header">Well done! Your first practice trade is in.</Text.Title>
          <Text.Body tone="secondary">
            {proposal.data && waiting && proposal.data.orderType === 'buy_stop'
              ? `It’s waiting for ${proposal.data.symbol} to reach ${formatPrice(proposal.data.entry)}. You can watch it in Activity, and once it buys it shows on Home.`
              : 'You can watch it on Home and in Activity. The stop and target are already set, so you don’t need to keep checking.'}
          </Text.Body>
        </View>
      </Screen>
    );
  }

  const p = proposal.data;
  const expired = proposal.isError || (p && (p.status !== 'pending' || Date.parse(p.expiresAt) <= now));
  if (expired) {
    return (
      <Screen
        header={header}
        footer={
          <View style={styles.footer}>
            <Button label="Pick a new idea" onPress={onNewIdea} loading={starting} testID="guided-new-idea" />
            <Button variant="link" label="Skip for now" onPress={onSkip} />
          </View>
        }
      >
        <View style={styles.content} testID="guided-expired">
          <Text.Title accessibilityRole="header">This idea has ended</Text.Title>
          <Text.Body tone="secondary">Trade ideas only last for the day. Let’s pick today’s.</Text.Body>
        </View>
      </Screen>
    );
  }
  if (!p) return <Screen header={header}>{null}</Screen>;

  const a = sizing.current;
  const index = COUNTED.indexOf(step as (typeof COUNTED)[number]);
  const gate = approveGate({ proposal: p, current: a, inputs: sizing.inputs, assessing: sizing.assessing, error: sizing.error, now });
  const back = () => to(index > 0 ? COUNTED[index - 1]! : 'intro');

  let body: React.ReactNode;
  let primary: React.ReactNode;
  if (step === 'approve') {
    body = (
      <>
        <Text.Title accessibilityRole="header">Approve it</Text.Title>
        <Text.Body tone="secondary">
          Last step. You’ll see every detail once more, then press and hold the button to approve. Holding instead of tapping means you
          can’t approve by accident.
        </Text.Body>
        {!gate.enabled ? (
          <Text.Body tone="secondary" testID="guided-gate">
            {gate.reason}
          </Text.Body>
        ) : null}
        <WhatIf action="approve" />
      </>
    );
    primary = gate.enabled ? (
      <Button label="Review and approve" onPress={() => setConfirmOpen(true)} testID="guided-review" />
    ) : gate.recheck ? (
      <Button label="Check again" onPress={() => recheck()} testID="guided-recheck" />
    ) : (
      <Button label="Try another idea" onPress={onNewIdea} loading={starting} testID="guided-new-idea" />
    );
  } else {
    const copy = stepCopy(step as 'stock' | 'plan' | 'stop' | 'risk', p, a);
    const blocked = step === 'risk' && !gate.enabled && !gate.recheck && gate.reason !== 'Checking the latest numbers…';
    body = (
      <>
        <Text.Title accessibilityRole="header">{copy.title}</Text.Title>
        {copy.body.map((line) => (
          <Text.Body key={line} tone="secondary">
            {line}
          </Text.Body>
        ))}
        {blocked ? (
          <Text.Body tone="secondary" testID="guided-gate">
            {`This one can’t be approved right now: ${gate.enabled ? '' : gate.reason}`}
          </Text.Body>
        ) : null}
      </>
    );
    primary = blocked ? (
      <Button label="Try another idea" onPress={onNewIdea} loading={starting} testID="guided-new-idea" />
    ) : (
      <Button label="Next" onPress={() => to(COUNTED[index + 1]!)} disabled={step === 'risk' && !a} testID="guided-next" />
    );
  }

  return (
    <>
      <Screen
        header={header}
        footer={
          <View style={styles.footer}>
            {primary}
            <View style={styles.row}>
              <Button variant="link" label="Back" block={false} onPress={back} testID="guided-back" />
              <Button variant="link" label="Skip for now" block={false} onPress={onSkip} testID="guided-skip" />
            </View>
          </View>
        }
      >
        <View style={styles.content} testID={`guided-${step}`}>
          <Text.Overline tone="tertiary">{`STEP ${index + 1} OF ${COUNTED.length} · ${p.symbol}`}</Text.Overline>
          <View
            style={styles.progress}
            accessibilityRole="progressbar"
            accessibilityValue={{ min: 0, max: COUNTED.length, now: index + 1 }}
          >
            <View style={[styles.progressFill, { width: `${((index + 1) / COUNTED.length) * 100}%` }]} />
          </View>
          {body}
        </View>
      </Screen>
      {a ? (
        <ConfirmOrderSheet
          open={confirmOpen}
          proposal={p}
          assessment={a}
          onClose={() => setConfirmOpen(false)}
          onPlaced={(order) => {
            setConfirmOpen(false);
            setPlaced(order);
            void queryClient.invalidateQueries({ queryKey: proposalKey(p.id) });
            void queryClient.invalidateQueries({ queryKey: ['positions'] });
            void queryClient.invalidateQueries({ queryKey: ['portfolio'] });
            to('done');
          }}
        />
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[4], paddingTop: space[4] },
  footer: { gap: space[2], paddingHorizontal: space[4], paddingTop: space[3], paddingBottom: space[2] },
  row: { flexDirection: 'row', justifyContent: 'space-between' },
  progress: { height: 4, borderRadius: radius.badge, backgroundColor: color.surface2, overflow: 'hidden' },
  progressFill: { height: 4, backgroundColor: color.signal },
});
