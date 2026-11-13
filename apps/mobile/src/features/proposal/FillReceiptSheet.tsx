import type { PaperOrder } from '@astryx/api-client';
import { space } from '@astryx/design-tokens';
import { StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Sheet } from '@/components/Sheet';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { formatEtTime, formatMoney, formatPrice, formatQuantity } from '@/lib/format';

/**
 * FillReceiptSheet (design 08): the executed receipt with order id, fill time, slippage, OCO exits and commission,
 * or "Order working · not filled" when a buy stop is waiting for its trigger.
 */
export function FillReceiptSheet({ order, onDone, onView }: { order: PaperOrder | null; onDone: () => void; onView: (where: 'portfolio' | 'activity') => void }) {
  const fill = order?.fill;
  return (
    <Sheet open={order !== null} onClose={onDone} accessibilityLabel={fill ? 'Simulated fill receipt' : 'Order working'}>
      {order ? (
        <View style={styles.body} testID="fill-receipt">
          {fill ? (
            <>
              <StatusBanner tone="executed" title="EXECUTED · SIMULATED FILL" orderId={order.brokerOrderId} />
              <Text.Headline accessibilityRole="header">{`Bought ${formatQuantity(order.quantity)} ${order.symbol} at ${formatMoney(fill.price)}`}</Text.Headline>
              <Row label="Order ID" value={order.brokerOrderId} />
              <Row label="Filled" value={`${formatEtTime(fill.filledAt)} · paper account`} />
              <Row label="Slippage" value={`+${formatMoney(fill.slippagePerShare)} vs entry ${formatPrice(order.stopPrice ?? order.limitPrice ?? fill.referencePrice)}`} />
              {order.attached ? <Row label="Stop / target" value={`OCO attached · ${formatPrice(order.attached.stop)} / ${formatPrice(order.attached.target)}`} /> : null}
              <Row label="Commission" value={formatMoney(fill.fee)} />
              <View style={styles.buttons}>
                <Button variant="secondary" label="Done" block={false} onPress={onDone} style={styles.flex} />
                <Button label="View in portfolio" block={false} onPress={() => onView('portfolio')} style={styles.flex} />
              </View>
            </>
          ) : (
            <>
              <StatusBanner tone="pending" title="ORDER WORKING · NOT FILLED" body={`${order.brokerOrderId} · triggers at ${formatPrice(order.stopPrice ?? order.limitPrice ?? '0')}. It expires at the close if not triggered.`} />
              <Text.Headline accessibilityRole="header">{`Buy ${formatQuantity(order.quantity)} ${order.symbol} is working`}</Text.Headline>
              <View style={styles.buttons}>
                <Button variant="secondary" label="Done" block={false} onPress={onDone} style={styles.flex} />
                <Button label="View in Activity" block={false} onPress={() => onView('activity')} style={styles.flex} />
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
