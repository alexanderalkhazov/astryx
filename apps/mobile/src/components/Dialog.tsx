import { color, elevation, radius, space, type as typeScale } from '@astryx/design-tokens';
import { Modal, StyleSheet, Text, type TextStyle, View } from 'react-native';

import { Button } from './Button';

export interface DialogProps {
  visible: boolean;
  title: string;
  body: string;
  /** Mono amber line, e.g. "SIMULATED · PAPER ACCOUNT". */
  overline?: string;
  cancelLabel: string;
  confirmLabel: string;
  onCancel: () => void;
  onConfirm: () => void;
  /** Destructive confirm renders solid red. */
  destructive?: boolean;
  /** While the action runs: confirm shows a spinner and the dialog cannot be dismissed. */
  busy?: boolean;
}

/**
 * Centred dialog, only for irreversible actions (design: "the destructive choice sits on the right and is never the
 * default focus"). Cancel comes first in reading and focus order; Android back and the backdrop cancel.
 */
export function Dialog({
  visible,
  title,
  body,
  overline,
  cancelLabel,
  confirmLabel,
  onCancel,
  onConfirm,
  destructive = false,
  busy = false,
}: DialogProps) {
  return (
    <Modal
      visible={visible}
      transparent
      animationType="fade"
      statusBarTranslucent
      onRequestClose={() => {
        if (!busy) onCancel();
      }}
    >
      <View style={styles.backdrop}>
        <View style={styles.card} accessibilityViewIsModal accessibilityRole="alert">
          <Text style={styles.title} accessibilityRole="header">
            {title}
          </Text>
          <Text style={styles.body}>{body}</Text>
          {overline ? <Text style={styles.overline}>{overline.toUpperCase()}</Text> : null}
          <View style={styles.actions}>
            <Button variant="secondary" label={cancelLabel} onPress={onCancel} disabled={busy} style={styles.action} />
            <Button
              variant={destructive ? 'danger' : 'primary'}
              label={confirmLabel}
              onPress={onConfirm}
              loading={busy}
              style={styles.action}
            />
          </View>
        </View>
      </View>
    </Modal>
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
  title: { ...(typeScale.headline as TextStyle), color: color.text },
  body: { ...(typeScale.body as TextStyle), color: color.text2 },
  overline: { ...(typeScale.overline as TextStyle), color: color.pending },
  actions: { flexDirection: 'row', gap: space[3], marginTop: space[2] },
  action: { flex: 1 },
});
