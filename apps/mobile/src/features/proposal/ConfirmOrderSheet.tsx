import { isApiError, type PaperOrder, type Proposal, type RiskAssessment } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { useMutation } from '@tanstack/react-query';
import { randomUUID } from 'expo-crypto';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { HoldToConfirm } from '@/components/HoldToConfirm';
import { Sheet } from '@/components/Sheet';
import { Text } from '@/components/Text';
import { WhatIf } from '@/features/plain/WhatIf';
import { paper } from '@/lib/api';
import { formatMoney, formatPrice, formatQuantity } from '@/lib/format';
import { toast } from '@/state/toasts';

const ORDER: Record<Proposal['orderType'], string> = { buy_stop: 'Buy stop', limit: 'Limit', market: 'Market' };

/**
 * ConfirmOrderSheet (design 07, SYSTEM_DESIGN.md §8.4.5): repeats every field, then hold-to-approve. The
 * Idempotency-Key is created when the sheet opens and reused for retries of the same confirm; while the order sends
 * the sheet cannot be dismissed. Errors unlock it and explain why; the proposal is unchanged.
 */
export function ConfirmOrderSheet({
  open,
  proposal,
  assessment,
  onClose,
  onPlaced,
}: {
  open: boolean;
  proposal: Proposal;
  assessment: RiskAssessment;
  onClose: () => void;
  onPlaced: (order: PaperOrder) => void;
}) {
  const [key, setKey] = useState(() => randomUUID());
  useEffect(() => {
    if (open) setKey(randomUUID()); // one key per confirm attempt
  }, [open]);

  const [attempt, setAttempt] = useState(0);
  const place = useMutation({
    mutationFn: async () => (await paper.place({ proposalId: proposal.id, assessmentId: assessment.id }, key)).data,
    onSuccess: onPlaced,
    onError: (error) => {
      setAttempt((n) => n + 1); // a fresh hold control: the user can hold again (same key)
      toast({
        tone: 'error',
        title: "The order wasn't placed",
        body: isApiError(error) ? (error.detail ?? error.message) : 'Check your connection and try again.',
      });
    },
  });
  const sending = place.isPending;
  const rows: [string, string][] = [
    ['Instrument', `${proposal.symbol} · ${proposal.name}`],
    ['Action', `Buy ${formatQuantity(assessment.quantity)} shares · long`],
    ['Order', `${ORDER[proposal.orderType]} @ ${formatPrice(proposal.entry)} · day`],
    ['Attached stop', `Stop loss @ ${formatPrice(proposal.stop)}`],
    ['Attached target', `Take profit @ ${formatPrice(proposal.target)}`],
    ['Capital required', formatMoney(assessment.capitalRequired)],
    ['Max loss', `−${formatMoney(assessment.maxLoss)}`],
  ];

  return (
    <Sheet open={open} onClose={onClose} dismissible={!sending} accessibilityLabel="Confirm simulated order">
      <View style={styles.body} testID="confirm-order-sheet">
        <Text.Overline tone="pending">PAPER ACCOUNT · SIMULATED FILL · NO REAL MONEY</Text.Overline>
        <Text.Headline accessibilityRole="header">Confirm simulated order</Text.Headline>
        <View style={styles.rows}>
          {rows.map(([label, value]) => (
            <View key={label} style={styles.row} accessible accessibilityLabel={`${label}: ${value}`}>
              <Text.Body tone="secondary">{label}</Text.Body>
              <Text.Data style={label === 'Max loss' ? styles.loss : undefined}>{value}</Text.Data>
            </View>
          ))}
        </View>
        <Text.Caption tone="secondary">
          The order is simulated against our quotes in your paper account. A buy stop waits until the price reaches its entry. The stop and
          target are attached as a one-cancels-the-other pair.
        </Text.Caption>
        <WhatIf action="approve" />
        <HoldToConfirm
          key={attempt}
          busy={sending}
          confirmTitle="Approve simulated trade?"
          confirmBody={`Buy ${formatQuantity(assessment.quantity)} ${proposal.symbol} in your paper account.`}
          onConfirm={() => place.mutate()}
          testID="hold-to-approve"
        />
        <Button variant="link" label="Cancel — keep as proposal" disabled={sending} onPress={onClose} />
      </View>
    </Sheet>
  );
}

const styles = StyleSheet.create({
  body: { gap: space[4], padding: space[4] },
  rows: { gap: space[2] },
  row: { flexDirection: 'row', justifyContent: 'space-between', gap: space[3] },
  loss: { color: color.loss },
});
