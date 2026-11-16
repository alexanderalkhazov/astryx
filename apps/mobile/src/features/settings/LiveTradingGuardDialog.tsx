import { isApiError, type RiskSettings } from '@astryx/api-client';
import { color, elevation, radius, space } from '@astryx/design-tokens';
import { useState } from 'react';
import { Modal, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Text } from '@/components/Text';
import { settings } from '@/lib/api';
import { formatPct } from '@/lib/format';

type Step = 'limits' | 'confirm' | 'unavailable';

/**
 * Live-trading guard (design 25, SYSTEM_DESIGN.md §8.4.11): re-confirm the risk limits, type LIVE, then ask the
 * server. In the MVP the server always refuses (403 `live_trading_disabled`), so the last step explains that the
 * account stays in paper mode. Biometric confirmation joins when live trading exists (§17.9).
 */
export function LiveTradingGuardDialog({
  visible,
  limits,
  onClose,
}: {
  visible: boolean;
  limits: RiskSettings | undefined;
  onClose: () => void;
}) {
  const [step, setStep] = useState<Step>('limits');
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("Live trading isn't available yet. Your account stays in paper mode.");

  const close = () => {
    setStep('limits');
    setTyped('');
    onClose();
  };

  const request = async () => {
    setBusy(true);
    try {
      await settings.setTradingMode('live', 'LIVE');
    } catch (error) {
      if (isApiError(error) && error.code === 'live_trading_disabled' && error.detail) setMessage(error.detail);
    } finally {
      setBusy(false);
      setStep('unavailable');
    }
  };

  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      statusBarTranslucent
      onRequestClose={() => {
        if (!busy) close();
      }}
    >
      <View style={styles.backdrop}>
        <View style={styles.card} accessibilityViewIsModal testID="live-guard">
          {step === 'limits' ? (
            <>
              <Text.Overline tone="pending">Real money</Text.Overline>
              <Text.Headline accessibilityRole="header">Switch to live trading?</Text.Headline>
              <Text.Body tone="secondary">Live orders use real money. Your limits apply to every order:</Text.Body>
              {limits ? (
                <View style={styles.limits}>
                  <Limit label="Max risk per trade" value={formatPct(limits.maxRiskPerTradePct)} />
                  <Limit label="Max position weight" value={formatPct(limits.maxPositionWeightPct, { dp: 0 })} />
                  <Limit label="Max sector exposure" value={formatPct(limits.maxSectorExposurePct, { dp: 0 })} />
                  <Limit label="Daily loss halt" value={`−${formatPct(limits.dailyLossHaltPct)}`} />
                  <Limit label="Max open positions" value={String(limits.maxOpenPositions)} />
                </View>
              ) : null}
              <View style={styles.actions}>
                <Button variant="secondary" label="Stay in paper mode" onPress={close} style={styles.action} />
                <Button label="Continue" onPress={() => setStep('confirm')} style={styles.action} />
              </View>
            </>
          ) : step === 'confirm' ? (
            <>
              <Text.Headline accessibilityRole="header">Type LIVE to confirm</Text.Headline>
              <Text.Body tone="secondary">Every approval would still need hold-to-confirm.</Text.Body>
              <Input
                value={typed}
                onChangeText={setTyped}
                autoCapitalize="characters"
                autoCorrect={false}
                accessibilityLabel="Type LIVE to confirm"
                placeholder="LIVE"
              />
              <View style={styles.actions}>
                <Button variant="secondary" label="Stay in paper mode" onPress={close} disabled={busy} style={styles.action} />
                <Button
                  variant="danger"
                  label="Switch to live"
                  disabled={typed.trim() !== 'LIVE'}
                  loading={busy}
                  onPress={() => void request()}
                  style={styles.action}
                />
              </View>
            </>
          ) : (
            <>
              <Text.Headline accessibilityRole="header">Live trading isn't available yet</Text.Headline>
              <Text.Body tone="secondary">{message}</Text.Body>
              <Button label="Stay in paper mode" onPress={close} />
            </>
          )}
        </View>
      </View>
    </Modal>
  );
}

function Limit({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.limit} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text.Body tone="secondary">{label}</Text.Body>
      <Text.Data>{value}</Text.Data>
    </View>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: space[4] },
  card: {
    backgroundColor: color.surface2,
    borderRadius: radius.dialog,
    borderWidth: 1,
    borderColor: color.lineStrong,
    padding: space[5],
    gap: space[3],
    ...elevation.e2,
  },
  limits: { gap: space[1] },
  limit: { flexDirection: 'row', justifyContent: 'space-between', minHeight: 28, alignItems: 'center' },
  actions: { flexDirection: 'row', gap: space[3], marginTop: space[2] },
  action: { flex: 1 },
});
