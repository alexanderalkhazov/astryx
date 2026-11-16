import { color, radius, space, type as typeScale } from '@astryx/design-tokens';
import { StyleSheet, Text, type TextStyle, View, type ViewStyle } from 'react-native';

import { Button } from './Button';

interface BannerBase {
  title: string;
  body?: string;
  action?: { label: string; onPress: () => void };
  testID?: string;
}

/**
 * Research vs. execution is encoded in the type (SYSTEM_DESIGN.md §8.1, §8.6): an executed banner must carry the order
 * ID, and a pending (research) banner can never show one.
 */
export type StatusBannerProps = BannerBase &
  (
    | { tone: 'pending'; orderId?: never }
    | { tone: 'executed'; orderId: string }
    | { tone: 'error'; orderId?: never }
    | { tone: 'rejected'; orderId?: never }
  );

const toneStyle: Record<StatusBannerProps['tone'], { box: ViewStyle; title: TextStyle; overline: boolean }> = {
  // Amber, dashed: research / not executed.
  pending: {
    box: { borderColor: 'rgba(242,181,68,0.6)', borderStyle: 'dashed', backgroundColor: 'rgba(242,181,68,0.06)' },
    title: { color: color.pending },
    overline: true,
  },
  // Solid green: executed, with order ID.
  executed: {
    box: { borderColor: 'rgba(61,214,140,0.45)', backgroundColor: 'rgba(61,214,140,0.07)' },
    title: { color: color.gain },
    overline: true,
  },
  error: {
    box: { borderColor: 'rgba(255,107,107,0.45)', backgroundColor: 'rgba(255,107,107,0.08)' },
    title: { color: color.text },
    overline: false,
  },
  rejected: { box: { borderColor: color.lineStrong, backgroundColor: color.surface1 }, title: { color: color.text2 }, overline: true },
};

export function StatusBanner(props: StatusBannerProps) {
  const { tone, title, body, action, testID } = props;
  const style = toneStyle[tone];
  return (
    <View
      testID={testID}
      accessible
      accessibilityRole={tone === 'error' ? 'alert' : 'summary'}
      accessibilityLabel={[title, body, props.orderId ? `Order ${props.orderId}` : undefined].filter(Boolean).join('. ')}
      style={[styles.box, style.box]}
    >
      <Text style={[style.overline ? styles.overline : styles.title, style.title]}>{style.overline ? title.toUpperCase() : title}</Text>
      {body ? <Text style={styles.body}>{body}</Text> : null}
      {tone === 'executed' ? <Text style={styles.orderId}>Order {props.orderId}</Text> : null}
      {action ? <Button variant="link" label={action.label} onPress={action.onPress} /> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  box: { borderWidth: 1, borderRadius: radius.card, padding: space[4], gap: space[1] },
  overline: { ...(typeScale.overline as TextStyle) },
  title: { ...(typeScale.section as TextStyle) },
  body: { ...(typeScale.body as TextStyle), color: color.text2 },
  orderId: { ...(typeScale.dataSm as TextStyle), color: color.text },
});
