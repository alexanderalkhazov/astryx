import type { PaperOrder } from '@astryx/api-client';
import { space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import { StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Sheet } from '@/components/Sheet';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { paper } from '@/lib/api';
import { formatEtTime, formatMoney, formatPrice, formatQuantity } from '@/lib/format';

/** An Alpaca order still on its way: committed, or accepted by Alpaca and not filled yet (§17.5). */
const inFlight = (order: PaperOrder) => order.status === 'submitting' || order.status === 'submitted';

/**
 * FillReceiptSheet (design 08): the executed receipt with order id, fill time, slippage, OCO exits and commission,
 * or "Order working · not filled" when a buy stop is waiting for its trigger.
 */
/**
 * `onClose` only clears the receipt (drag-down, backdrop, or the sheet finishing its dismiss); navigation happens in
 * `onDone` / `onView` alone, so closing never navigates a second time.
 */
export function FillReceiptSheet({
  order,
  onClose,
  onDone,
  onView,
}: {
  order: PaperOrder | null;
  onClose: () => void;
  onDone: () => void;
  onView: (where: 'portfolio' | 'activity') => void;
}) {
  // Alpaca decides the fill (§17.5): follow the order until Alpaca fills, rests or rejects it.
  const live = useQuery({
    queryKey: ['paper', 'order', order?.id],
    queryFn: async () => (await paper.get(order!.id)).data,
    enabled: order?.broker === 'alpaca',
    initialData: order ?? undefined,
    refetchInterval: (query) => (query.state.data && inFlight(query.state.data) ? 1000 : false),
  });
  const shown = order && live.data?.id === order.id ? live.data : order;
  const fill = shown?.fill;
  const alpaca = shown?.broker === 'alpaca';
  return (
    <Sheet
      open={order !== null}
      onClose={onClose}
      accessibilityLabel={fill ? (alpaca ? 'Alpaca fill receipt' : 'Simulated fill receipt') : 'Order working'}
    >
      {shown ? (
        <View style={styles.body} testID="fill-receipt">
          {inFlight(shown) ? (
            <>
              <StatusBanner tone="pending" title="SENDING TO ALPACA…" body="Your paper order is with Alpaca, which decides the fill." />
              <Text.Headline accessibilityRole="header">{`Buy ${formatQuantity(shown.quantity)} ${shown.symbol}`}</Text.Headline>
              <View style={styles.buttons}>
                <Button variant="secondary" label="Done" block={false} onPress={onDone} style={styles.flex} />
                <Button
                  label="View in Activity"
                  block={false}
                  onPress={() => onView('activity')}
                  style={styles.flex}
                  testID="receipt-view-activity"
                />
              </View>
            </>
          ) : shown.status === 'rejected' || shown.status === 'cancelled' || shown.status === 'expired' ? (
            <>
              <StatusBanner
                tone="error"
                title={`ORDER ${shown.status.toUpperCase()} · NOT FILLED`}
                body="Alpaca didn't fill it. Nothing was bought and the reserved cash is free again."
              />
              <Text.Headline accessibilityRole="header">{`Buy ${formatQuantity(shown.quantity)} ${shown.symbol} wasn't filled`}</Text.Headline>
              <Button variant="secondary" label="Done" onPress={onDone} />
            </>
          ) : fill ? (
            <>
              <StatusBanner
                tone="executed"
                title={alpaca ? 'EXECUTED · FILLED BY ALPACA (PAPER)' : 'EXECUTED · SIMULATED FILL'}
                orderId={shown.brokerOrderId}
              />
              <Text.Headline accessibilityRole="header">{`Bought ${formatQuantity(shown.quantity)} ${shown.symbol} at ${formatMoney(fill.price)}`}</Text.Headline>
              <Row label="Order ID" value={shown.brokerOrderId} />
              <Row label="Filled" value={`${formatEtTime(fill.filledAt)} · ${alpaca ? 'Alpaca paper' : 'paper account'}`} />
              <Row
                label="Slippage"
                value={`+${formatMoney(fill.slippagePerShare)} vs entry ${formatPrice(shown.stopPrice ?? shown.limitPrice ?? fill.referencePrice)}`}
              />
              {shown.attached ? (
                <Row
                  label="Stop / target"
                  value={`${alpaca ? 'Alpaca bracket' : 'OCO attached'} · ${formatPrice(shown.attached.stop)} / ${formatPrice(shown.attached.target)}`}
                />
              ) : null}
              <Row label="Commission" value={formatMoney(fill.fee)} />
              <View style={styles.buttons}>
                <Button variant="secondary" label="Done" block={false} onPress={onDone} style={styles.flex} />
                <Button
                  label="View in portfolio"
                  block={false}
                  onPress={() => onView('portfolio')}
                  style={styles.flex}
                  testID="receipt-view-portfolio"
                />
              </View>
            </>
          ) : (
            <>
              <StatusBanner
                tone="pending"
                title="ORDER WORKING · NOT FILLED"
                body={`${shown.brokerOrderId}${alpaca ? ' · resting at Alpaca' : ''} · triggers at ${formatPrice(shown.stopPrice ?? shown.limitPrice ?? '0')}. It expires at the close if not triggered.`}
              />
              <Text.Headline accessibilityRole="header">{`Buy ${formatQuantity(shown.quantity)} ${shown.symbol} is working`}</Text.Headline>
              <View style={styles.buttons}>
                <Button variant="secondary" label="Done" block={false} onPress={onDone} style={styles.flex} />
                <Button
                  label="View in Activity"
                  block={false}
                  onPress={() => onView('activity')}
                  style={styles.flex}
                  testID="receipt-view-activity"
                />
              </View>
            </>
          )}
        </View>
      ) : null}
    </Sheet>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text.Body tone="secondary">{label}</Text.Body>
      <Text.Data>{value}</Text.Data>
    </View>
  );
}

const styles = StyleSheet.create({
  body: { gap: space[3], padding: space[4] },
  row: { flexDirection: 'row', justifyContent: 'space-between', gap: space[3] },
  buttons: { flexDirection: 'row', gap: space[3], marginTop: space[2] },
  flex: { flex: 1 },
});
