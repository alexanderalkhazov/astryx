import { color, radius, space } from '@astryx/design-tokens';
import Decimal from 'decimal.js-light';
import { StyleSheet, View } from 'react-native';

import { Text } from '@/components/Text';
import { formatMoney, formatRatio } from '@/lib/format';

/**
 * The R:R bar (design 06): loss at stop on the left, gain at target on the right, widths in proportion, and
 * "Risk : reward 1 : 2.11" between them. Values are server figures; the widths are drawing only.
 */
export function RewardRiskBar({ maxLoss, potentialProfit, rewardRisk }: { maxLoss: string; potentialProfit: string; rewardRisk: string }) {
  const loss = new Decimal(maxLoss);
  const gain = new Decimal(potentialProfit);
  const total = loss.plus(gain);
  const lossShare = total.isZero() ? 0.5 : loss.div(total).toNumber(); // layout only
  return (
    <View
      style={styles.wrap}
      accessible
      accessibilityLabel={`Loss at stop ${formatMoney(maxLoss)}, gain at target ${formatMoney(potentialProfit)}, risk to reward 1 to ${formatRatio(rewardRisk)}`}
      testID="rr-bar"
    >
      <View style={styles.track}>
        <View style={[styles.loss, { flex: lossShare }]} />
        <View style={[styles.gain, { flex: 1 - lossShare }]} />
      </View>
      <View style={styles.labels}>
        <View>
          <Text.Caption tone="tertiary">Loss at stop</Text.Caption>
          <Text.Data tone="loss">{`−${formatMoney(maxLoss)}`}</Text.Data>
        </View>
        <View style={styles.center}>
          <Text.Caption tone="tertiary">Risk : reward</Text.Caption>
          <Text.Data>{`1 : ${formatRatio(rewardRisk)}`}</Text.Data>
        </View>
        <View style={styles.right}>
          <Text.Caption tone="tertiary">Gain at target</Text.Caption>
          <Text.Data tone="gain">{`+${formatMoney(potentialProfit)}`}</Text.Data>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: space[2] },
  track: { flexDirection: 'row', height: 8, borderRadius: radius.chip, overflow: 'hidden', gap: 2 },
  loss: { backgroundColor: color.loss },
  gain: { backgroundColor: color.gain },
  labels: { flexDirection: 'row', justifyContent: 'space-between' },
  center: { alignItems: 'center' },
  right: { alignItems: 'flex-end' },
});
