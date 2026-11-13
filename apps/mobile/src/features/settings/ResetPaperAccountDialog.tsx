import { isApiError } from '@astryx/api-client';
import { color, elevation, radius, space } from '@astryx/design-tokens';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { randomUUID } from 'expo-crypto';
import { useState } from 'react';
import { Modal, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Input } from '@/components/Input';
import { Text } from '@/components/Text';
import { MONEY_QUERY_KEYS } from '@/features/realtime/realtime';
import { portfolio } from '@/lib/api';
import { formatMoney } from '@/lib/format';
import { toast } from '@/state/toasts';

/**
 * PROPOSED reset dialog (Settings › Paper account, §11.8 P11): type RESET, then every position closes at its last
 * price, working orders are cancelled, cash returns to the starting balance and performance starts a new epoch.
 * One idempotency key per opened dialog, so a retried tap resets once.
 */
export function ResetPaperAccountDialog({ visible, onClose }: { visible: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [typed, setTyped] = useState('');
  const [key, setKey] = useState(() => randomUUID());

  const close = () => {
    setTyped('');
    setKey(randomUUID());
    onClose();
  };

  const reset = useMutation({
    mutationFn: async () => (await portfolio.reset(key)).data,
    onSuccess: (data) => {
      for (const k of [...MONEY_QUERY_KEYS, ['paper']]) void queryClient.invalidateQueries({ queryKey: k });
      toast({
        tone: 'success',
        title: 'Paper account reset',
        body: `Closed ${data.closedPositions} ${data.closedPositions === 1 ? 'position' : 'positions'}. Cash is back to ${formatMoney(data.startingCash, { dp: 0 })}.`,
      });
      close();
    },
    onError: (error) => toast({ tone: 'error', title: "The account wasn't reset", body: isApiError(error) ? (error.detail ?? error.message) : 'Try again in a moment.' }),
  });

  return (
    <Modal visible={visible} transparent animationType="fade" statusBarTranslucent onRequestClose={() => {
      if (!reset.isPending) close();
    }}>
      <View style={styles.backdrop}>
        <View style={styles.card} accessibilityViewIsModal testID="reset-dialog">
          <Text.Headline accessibilityRole="header">Reset paper account?</Text.Headline>
          <Text.Body tone="secondary">
            Every open position closes at its last price, working orders are cancelled and cash returns to the starting balance. Performance and trade history start fresh. This can't be undone.
          </Text.Body>
          <Input value={typed} onChangeText={setTyped} autoCapitalize="characters" autoCorrect={false} accessibilityLabel="Type RESET to confirm" placeholder="RESET" />
          <View style={styles.actions}>
            <Button variant="secondary" label="Cancel" onPress={close} disabled={reset.isPending} style={styles.action} />
            <Button variant="danger" label="Reset" disabled={typed.trim() !== 'RESET'} loading={reset.isPending} onPress={() => reset.mutate()} style={styles.action} />
          </View>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.6)', justifyContent: 'center', padding: space[4] },
  card: { backgroundColor: color.surface2, borderRadius: radius.dialog, borderWidth: 1, borderColor: color.lineStrong, padding: space[5], gap: space[3], ...elevation.e2 },
  actions: { flexDirection: 'row', gap: space[3], marginTop: space[2] },
  action: { flex: 1 },
});
