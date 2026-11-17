import { isApiError, type NotificationPreferences, type RiskSettings, type RiskSettingsUpdate } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { router } from 'expo-router';
import { ChevronRight } from 'lucide-react-native';
import { type ReactNode, useEffect, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Badge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { Dialog } from '@/components/Dialog';
import { SkeletonRow } from '@/components/Feedback';
import { StackHeader } from '@/components/Navigation';
import { Segmented } from '@/components/Segmented';
import { Screen } from '@/components/Shell';
import { Stepper } from '@/components/Stepper';
import { Switch } from '@/components/Switch';
import { Text } from '@/components/Text';
import { registerForPush, unregisterPush } from '@/features/push/push';
import { LiveTradingGuardDialog } from '@/features/settings/LiveTradingGuardDialog';
import { ResetPaperAccountDialog } from '@/features/settings/ResetPaperAccountDialog';
import { useDefaultWatchlist } from '@/features/watchlist/useWatchlist';
import { portfolio, settings } from '@/lib/api';
import { formatCompactMoney, formatEtTime, formatMoney } from '@/lib/format';
import { signOut } from '@/lib/session';
import { toast } from '@/state/toasts';

// Designs 18 and 25 · SYSTEM_DESIGN.md §8.4.11. Paper is the only trading mode; live ends in the server's 403.
export default function SettingsScreen() {
  const [guardOpen, setGuardOpen] = useState(false);
  const risk = useQuery({ queryKey: ['settings', 'risk'], queryFn: async () => (await settings.risk()).data });

  return (
    <Screen header={<StackHeader title="Settings" />}>
      <BrokerSection />

      <Section title="Trading mode">
        <Segmented
          accessibilityLabel="Trading mode"
          mono={false}
          options={[
            { value: 'paper', label: 'Paper' },
            { value: 'live', label: 'Live' },
          ]}
          value="paper"
          onChange={(mode) => mode === 'live' && setGuardOpen(true)}
        />
        <Text.Caption tone="tertiary">
          Paper mode simulates fills at the proposal entry plus slippage. Every approval still requires hold-to-confirm.
        </Text.Caption>
      </Section>
      <LiveTradingGuardDialog visible={guardOpen} limits={risk.data} onClose={() => setGuardOpen(false)} />

      <Section title="Risk limits">{risk.data ? <RiskLimits current={risk.data} /> : <SkeletonRow />}</Section>
      <ScannerSection />
      <WatchlistRow />
      <NotificationsSection />
      <SecuritySection />
      <PaperAccountSection />
      {__DEV__ ? <PrototypeControls /> : null}
    </Screen>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <Text.Overline tone="tertiary">{title}</Text.Overline>
      {children}
    </View>
  );
}

function Row({ label, value, testID }: { label: string; value: ReactNode; testID?: string }) {
  return (
    <View style={styles.row} testID={testID}>
      <Text.Body tone="secondary" style={styles.flex}>
        {label}
      </Text.Body>
      {typeof value === 'string' ? <Text.Data>{value}</Text.Data> : value}
    </View>
  );
}

function BrokerSection() {
  const summary = useQuery({ queryKey: ['portfolio', 'summary'], queryFn: async () => (await portfolio.summary()).data });
  return (
    <Section title="Broker connection">
      <View style={styles.card} testID="broker-card">
        <View style={styles.row}>
          <View style={styles.flex}>
            <Text.Section>Paper account (simulated)</Text.Section>
            <Text.Caption tone="secondary">
              {summary.data ? `Connected · synced ${formatEtTime(summary.data.asOf)}` : 'Connected'}
            </Text.Caption>
          </View>
          <Badge label="PAPER" tone="pending" mono />
        </View>
        {summary.data ? <Row label="Equity" value={formatMoney(summary.data.equity)} /> : null}
        <View style={styles.divider} />
        <Text.Overline tone="tertiary">Permissions</Text.Overline>
        <Row label="Read portfolio & balances" value={<Text.DataSm tone="gain">Allowed</Text.DataSm>} />
        <Row label="Place & close orders" value={<Text.DataSm tone="pending">Paper only</Text.DataSm>} />
        <Row
          label="Deposits & withdrawals"
          value={<Text.DataSm tone="secondary">Never requested</Text.DataSm>}
          testID="permission-transfers"
        />
        <Button variant="secondary" label="Connect Alpaca" disabled />
        <Text.Caption tone="tertiary">
          Connecting an Alpaca paper account comes in a later release. Astryx never moves money in or out of an account.
        </Text.Caption>
      </View>
    </Section>
  );
}

type RiskField = 'maxRiskPerTradePct' | 'maxPositionWeightPct' | 'maxSectorExposurePct' | 'dailyLossHaltPct';

/** Percent fields are edited in percent units (0.75) and stored as fractions (0.0075). */
const RISK_FIELDS: readonly { key: RiskField; label: string; step: string; prefix?: string }[] = [
  { key: 'maxRiskPerTradePct', label: 'Max risk per trade', step: '0.05' },
  { key: 'maxPositionWeightPct', label: 'Max position weight', step: '1' },
  { key: 'maxSectorExposurePct', label: 'Max sector exposure', step: '1' },
  { key: 'dailyLossHaltPct', label: 'Daily loss halt', step: '0.25', prefix: '−' },
];

const toPercent = (fraction: string) => new Decimal(fraction).times(100).toString();
const toFraction = (percent: string) => new Decimal(percent).div(100).toString();

function RiskLimits({ current }: { current: RiskSettings }) {
  const queryClient = useQueryClient();
  const initial = (): RiskSettingsUpdate => ({
    maxRiskPerTradePct: current.maxRiskPerTradePct,
    maxPositionWeightPct: current.maxPositionWeightPct,
    maxSectorExposurePct: current.maxSectorExposurePct,
    sectorWarningPct: current.sectorWarningPct,
    dailyLossHaltPct: current.dailyLossHaltPct,
    maxOpenPositions: current.maxOpenPositions,
    minRewardRisk: current.minRewardRisk,
  });
  const [draft, setDraft] = useState<RiskSettingsUpdate>(initial);
  // eslint-disable-next-line react-hooks/exhaustive-deps -- reset the draft only when the saved settings change version
  useEffect(() => setDraft(initial()), [current.version]);

  const dirty =
    RISK_FIELDS.some((f) => !new Decimal(draft[f.key]).equals(new Decimal(current[f.key]))) ||
    draft.maxOpenPositions !== current.maxOpenPositions;
  // The sector warning must stay below the sector cap (S2); keep the design's 5-point gap when the cap moves.
  const sectorWarning = (cap: string) => {
    const warning = new Decimal(current.sectorWarningPct);
    return warning.lessThan(new Decimal(cap)) ? current.sectorWarningPct : new Decimal(cap).minus('0.05').toString();
  };

  const save = useMutation({
    mutationFn: async () =>
      (await settings.updateRisk(current, { ...draft, sectorWarningPct: sectorWarning(draft.maxSectorExposurePct) })).data,
    onSuccess: (data) => {
      queryClient.setQueryData(['settings', 'risk'], data);
      toast({ tone: 'success', title: 'Risk limits saved', body: 'New assessments use them from now on.' });
    },
    onError: (error) => {
      if (isApiError(error) && error.status === 412) {
        void queryClient.invalidateQueries({ queryKey: ['settings', 'risk'] });
        toast({ tone: 'error', title: 'Limits changed elsewhere', body: 'We reloaded the latest values. Make your change again.' });
      } else {
        toast({
          tone: 'error',
          title: "Limits weren't saved",
          body: isApiError(error) ? (error.detail ?? error.message) : 'Try again in a moment.',
        });
      }
    },
  });

  return (
    <View style={styles.card} testID="risk-limits">
      {RISK_FIELDS.map((f) => {
        const bounds = current.bounds[f.key];
        return (
          <View key={f.key} style={styles.stepperRow}>
            <View style={styles.flex}>
              <Text.Body>{f.label}</Text.Body>
              <Text.Caption tone="tertiary">{`${f.prefix ?? ''}${toPercent(bounds.min)}–${toPercent(bounds.max)}%`}</Text.Caption>
            </View>
            <Stepper
              value={toPercent(draft[f.key])}
              min={toPercent(bounds.min)}
              max={toPercent(bounds.max)}
              step={f.step}
              unit="%"
              accessibilityLabel={f.label}
              onChange={(v) => setDraft((d) => ({ ...d, [f.key]: toFraction(v) }))}
            />
          </View>
        );
      })}
      <View style={styles.stepperRow}>
        <View style={styles.flex}>
          <Text.Body>Max open positions</Text.Body>
          <Text.Caption tone="tertiary">{`${current.bounds.maxOpenPositions.min}–${current.bounds.maxOpenPositions.max}`}</Text.Caption>
        </View>
        <Stepper
          value={String(draft.maxOpenPositions)}
          min={String(current.bounds.maxOpenPositions.min)}
          max={String(current.bounds.maxOpenPositions.max)}
          unit="positions"
          accessibilityLabel="Max open positions"
          onChange={(v) => setDraft((d) => ({ ...d, maxOpenPositions: Number(v) }))}
        />
      </View>
      <Text.Caption tone="tertiary">The risk engine enforces these on every assessment. AI suggestions never override them.</Text.Caption>
      <View style={styles.actionsRow}>
        <Button
          variant="secondary"
          label="Reset"
          disabled={!dirty || save.isPending}
          onPress={() => setDraft(initial())}
          style={styles.flex}
        />
        <Button label="Save limits" disabled={!dirty} loading={save.isPending} onPress={() => save.mutate()} style={styles.flex} />
      </View>
    </View>
  );
}

function ScannerSection() {
  const scanner = useQuery({ queryKey: ['settings', 'scanner'], queryFn: async () => (await settings.scanner()).data });
  const s = scanner.data;
  return (
    <Section title="Scanner">
      {s ? (
        <View style={styles.card} testID="scanner-settings">
          <Row label="Universe" value={s.universes.find((u) => u.code === s.universe)?.label ?? s.universe} />
          <Row
            label="Min price · avg $ volume"
            value={`${formatMoney(s.minPrice, { dp: 0 })} · ${formatCompactMoney(s.minAvgDollarVolume).replace(/\.00(?=[KMBT])/, '')}`}
          />
          <Row label="Min score" value={s.minScore ? `${s.minScore}+` : 'Any'} />
          <Row label="Signal types" value={`All ${s.signalTypes.length}`} />
          <Text.Caption tone="tertiary">The scanner is shared by every account. Narrow what you see with Discover's filters.</Text.Caption>
        </View>
      ) : (
        <SkeletonRow />
      )}
    </Section>
  );
}

function WatchlistRow() {
  const watchlist = useDefaultWatchlist();
  const count = watchlist.data?.items.length;
  return (
    <Section title="Watchlist">
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={count === undefined ? 'Watchlist, Manage' : `Watchlist, ${count} ${count === 1 ? 'stock' : 'stocks'}, Manage`}
        onPress={() => router.push('/settings/watchlist')}
        testID="settings-watchlist"
        style={({ pressed }) => [styles.row, styles.link, { opacity: pressed ? 0.7 : 1 }]}
      >
        <Text.Body style={styles.flex}>Watchlist</Text.Body>
        {count === undefined ? null : <Text.Data tone="secondary">{count}</Text.Data>}
        <Text.Body tone="secondary">Manage</Text.Body>
        <ChevronRight size={18} strokeWidth={1.7} color={color.text3} />
      </Pressable>
    </Section>
  );
}

const NOTIFICATION_ROWS: readonly { key: keyof Omit<NotificationPreferences, 'pushEnabled'>; label: string }[] = [
  { key: 'fills', label: 'Fills' },
  { key: 'signals', label: 'Signals' },
  { key: 'stopProximity', label: 'Stop proximity' },
  { key: 'strategyGuard', label: 'Strategy guard' },
  { key: 'system', label: 'System' },
  { key: 'dailySummary', label: 'Daily summary' },
];

const PUSH_UNAVAILABLE = {
  simulator: 'Push notifications need a physical device.',
  denied: 'Notifications are off for Astryx. Turn them on in your phone settings.',
  no_project: 'This build has no push project configured.',
  failed: "Couldn't register this device. Try again in a moment.",
} as const;

function NotificationsSection() {
  const queryClient = useQueryClient();
  const prefs = useQuery({ queryKey: ['settings', 'notifications'], queryFn: async () => (await settings.notifications()).data });
  const update = useMutation({
    mutationFn: async (next: NotificationPreferences) => (await settings.updateNotifications(next)).data,
    onMutate: (next) => {
      const previous = queryClient.getQueryData<NotificationPreferences>(['settings', 'notifications']);
      queryClient.setQueryData(['settings', 'notifications'], next);
      return { previous };
    },
    onError: (_error, _next, context) => {
      queryClient.setQueryData(['settings', 'notifications'], context?.previous);
      toast({ tone: 'error', title: "Preference wasn't saved", body: 'Try again in a moment.' });
    },
    onSuccess: (data) => queryClient.setQueryData(['settings', 'notifications'], data),
  });
  const p = prefs.data;
  // Turning push on asks for permission and registers this device (N4); off stops pushes to it (N5).
  const togglePush = async (on: boolean) => {
    if (!p) return;
    if (on) {
      const registered = await registerForPush();
      if (!registered.ok) {
        toast({ tone: 'error', title: "Push isn't available", body: PUSH_UNAVAILABLE[registered.reason] });
        return;
      }
    } else {
      await unregisterPush();
    }
    update.mutate({ ...p, pushEnabled: on });
  };
  return (
    <Section title="Notifications">
      {p ? (
        <View style={styles.card}>
          <Row
            label="Push notifications"
            value={<Switch value={p.pushEnabled} accessibilityLabel="Push notifications" onValueChange={(v) => void togglePush(v)} />}
            testID="push-master"
          />
          {NOTIFICATION_ROWS.map((r) => (
            <Row
              key={r.key}
              label={r.label}
              value={<Switch value={p[r.key]} accessibilityLabel={r.label} onValueChange={(v) => update.mutate({ ...p, [r.key]: v })} />}
            />
          ))}
          <Text.Caption tone="tertiary">
            The categories choose what reaches this device as a push. Activity always lists everything.
          </Text.Caption>
        </View>
      ) : (
        <SkeletonRow />
      )}
    </Section>
  );
}

function SecuritySection() {
  const [confirmAll, setConfirmAll] = useState(false);
  const all = useMutation({
    mutationFn: async () => {
      await settings.logoutAll();
    },
    onSettled: async () => {
      setConfirmAll(false);
      await signOut();
    },
  });
  return (
    <Section title="Security">
      <View style={styles.card}>
        <Pressable
          accessibilityRole="button"
          onPress={() => router.push('/settings/change-password')}
          style={({ pressed }) => [styles.row, styles.link, { opacity: pressed ? 0.7 : 1 }]}
        >
          <Text.Body style={styles.flex}>Change password</Text.Body>
          <ChevronRight size={18} strokeWidth={1.7} color={color.text3} />
        </Pressable>
        <Button variant="secondary" label="Sign out" onPress={() => void signOut()} />
        <Button variant="link" label="Sign out of all devices" onPress={() => setConfirmAll(true)} />
      </View>
      <Dialog
        visible={confirmAll}
        title="Sign out of all devices?"
        body="Every device signed in to this account, this one included, will need to sign in again."
        cancelLabel="Cancel"
        confirmLabel="Sign out all"
        destructive
        busy={all.isPending}
        onCancel={() => setConfirmAll(false)}
        onConfirm={() => all.mutate()}
      />
    </Section>
  );
}

function PaperAccountSection() {
  const [open, setOpen] = useState(false);
  return (
    <Section title="Paper account">
      <View style={styles.card}>
        <Text.Body tone="secondary">
          Start over with the starting cash. Open positions close at their last price and performance starts a new period.
        </Text.Body>
        <Button variant="danger" label="Reset paper account" onPress={() => setOpen(true)} />
      </View>
      <ResetPaperAccountDialog visible={open} onClose={() => setOpen(false)} />
    </Section>
  );
}

const FAULTS = ['ok', 'down', 'stale', 'bad_tick'] as const;

/** Dev builds only (§8.4.11): drives the fake market data provider through /dev/faults (Development API only). */
function PrototypeControls() {
  const faults = useQuery({ queryKey: ['dev', 'faults'], queryFn: async () => (await settings.devFaults()).data, retry: false });
  const set = useMutation({
    mutationFn: async (mode: (typeof FAULTS)[number]) => (await settings.setDevFaults(mode)).data,
    onSuccess: () => void faults.refetch(),
    onError: () =>
      toast({ tone: 'error', title: 'Fault switch unavailable', body: 'The API needs Development with Features:DevFaultInjection=true.' }),
  });
  return (
    <Section title="Prototype controls (dev)">
      <View style={[styles.card, styles.devCard]} testID="prototype-controls">
        <Text.Body tone="secondary">Market data feed</Text.Body>
        <Segmented
          accessibilityLabel="Market data fault"
          options={FAULTS.map((f) => ({ value: f, label: f }))}
          value={faults.data?.marketData ?? 'ok'}
          onChange={(mode) => set.mutate(mode)}
        />
        {faults.isError ? <Text.Caption tone="pending">Fault injection is off on this API.</Text.Caption> : null}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  section: { gap: space[2] },
  card: {
    gap: space[3],
    padding: space[4],
    borderRadius: radius.card,
    backgroundColor: color.surface1,
    borderWidth: 1,
    borderColor: color.line,
  },
  devCard: { borderStyle: 'dashed', borderColor: color.pending },
  row: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 36 },
  link: { minHeight: 44 },
  flex: { flex: 1 },
  divider: { height: 1, backgroundColor: color.line },
  stepperRow: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 56 },
  actionsRow: { flexDirection: 'row', gap: space[3] },
});
