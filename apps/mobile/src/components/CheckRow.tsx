import { color, radius, size, space, type as typeScale } from '@astryx/design-tokens';
import { Check, X } from 'lucide-react-native';
import { ActivityIndicator, StyleSheet, Text, type TextStyle, View } from 'react-native';

export type CheckStatus = 'pass' | 'warn' | 'fail' | 'pending';

export interface CheckRowProps {
  status: CheckStatus;
  label: string;
  detail?: string;
  /** Hide the top divider for the first row in a group. */
  first?: boolean;
}

const statusWord: Record<CheckStatus, string> = { pass: 'Passed', warn: 'Warning', fail: 'Failed', pending: 'Checking' };

/** Pre-trade check row (design 03 · "pass, warn and fail check rows"). */
export function CheckRow({ status, label, detail, first = false }: CheckRowProps) {
  return (
    <View
      accessible
      accessibilityLabel={`${statusWord[status]}: ${label}${detail ? `. ${detail}` : ''}`}
      style={[styles.row, first ? null : styles.divider]}
    >
      <StatusIcon status={status} />
      <View style={styles.text}>
        <Text style={styles.label}>{label}</Text>
        {detail ? <Text style={[styles.detail, status === 'fail' ? { color: color.loss } : null]}>{detail}</Text> : null}
      </View>
    </View>
  );
}

function StatusIcon({ status }: { status: CheckStatus }) {
  if (status === 'pending') {
    return (
      <View style={[styles.icon, { backgroundColor: color.surface2 }]}>
        <ActivityIndicator size="small" color={color.text2} />
      </View>
    );
  }

  const tint = status === 'pass' ? color.gain : status === 'warn' ? color.pending : color.loss;
  const background = status === 'pass' ? 'rgba(61,214,140,0.12)' : status === 'warn' ? 'rgba(242,181,68,0.12)' : 'rgba(255,107,107,0.12)';
  return (
    <View style={[styles.icon, { backgroundColor: background }]}>
      {status === 'pass' ? <Check size={14} strokeWidth={2} color={tint} /> : null}
      {status === 'fail' ? <X size={14} strokeWidth={2} color={tint} /> : null}
      {status === 'warn' ? <Text style={[styles.bang, { color: tint }]}>!</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: size.row, paddingHorizontal: space[4] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
  icon: { width: 24, height: 24, borderRadius: radius.button, alignItems: 'center', justifyContent: 'center' },
  bang: { ...(typeScale.dataSm as TextStyle), fontFamily: 'GeistMono_600SemiBold' },
  text: { flex: 1, gap: 2, paddingVertical: space[2] },
  label: { ...(typeScale.body as TextStyle), lineHeight: undefined, color: color.text },
  detail: { ...(typeScale.caption as TextStyle), color: color.text2 },
});
