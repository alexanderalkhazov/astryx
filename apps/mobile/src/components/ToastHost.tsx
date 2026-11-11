import { color, elevation, radius, space, type as typeScale } from '@astryx/design-tokens';
import { AlertTriangle, Check, Info, X } from 'lucide-react-native';
import { Pressable, StyleSheet, Text, type TextStyle, View } from 'react-native';
import Animated, { FadeInUp, FadeOutUp } from 'react-native-reanimated';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { type Toast, type ToastTone, useToasts } from '@/state/toasts';

const toneIcon: Record<ToastTone, { Icon: typeof Check; fg: string; bg: string }> = {
  success: { Icon: Check, fg: color.gain, bg: 'rgba(61,214,140,0.14)' },
  info: { Icon: Info, fg: color.signal, bg: 'rgba(138,180,255,0.14)' },
  warning: { Icon: AlertTriangle, fg: color.pending, bg: 'rgba(242,181,68,0.14)' },
  error: { Icon: X, fg: color.loss, bg: 'rgba(255,107,107,0.14)' },
};

/** Toasts slide in from the top, last 3.8 s and dismiss on tap (design: Notification toast). Mounted once at the root. */
export function ToastHost() {
  const toasts = useToasts((s) => s.toasts);
  const dismiss = useToasts((s) => s.dismiss);
  const insets = useSafeAreaInsets();
  if (toasts.length === 0) return null;
  return (
    <View pointerEvents="box-none" style={[styles.host, { top: insets.top + space[2] }]}>
      {toasts.map((t) => (
        <ToastCard key={t.id} toast={t} onDismiss={() => dismiss(t.id)} />
      ))}
    </View>
  );
}

function ToastCard({ toast, onDismiss }: { toast: Toast; onDismiss: () => void }) {
  const { Icon, fg, bg } = toneIcon[toast.tone];
  return (
    <Animated.View entering={FadeInUp} exiting={FadeOutUp}>
      <Pressable
        accessibilityRole="alert"
        accessibilityLiveRegion="polite"
        accessibilityLabel={`${toast.title}${toast.body ? `. ${toast.body}` : ''}`}
        accessibilityHint="Tap to dismiss"
        onPress={onDismiss}
        style={({ pressed }) => [styles.card, { opacity: pressed ? 0.85 : 1 }]}
      >
        <View style={[styles.icon, { backgroundColor: bg }]}>
          <Icon size={16} strokeWidth={2} color={fg} />
        </View>
        <View style={styles.text}>
          <Text style={styles.title}>{toast.title}</Text>
          {toast.body ? <Text style={styles.body}>{toast.body}</Text> : null}
        </View>
        <Text style={styles.time}>now</Text>
      </Pressable>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  host: { position: 'absolute', left: space[3], right: space[3], gap: space[2], zIndex: 1000 },
  card: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: space[3],
    padding: space[3],
    borderRadius: radius.card + 2,
    borderWidth: 1,
    borderColor: color.lineStrong,
    backgroundColor: color.surface2,
    ...elevation.e1,
  },
  icon: { width: 32, height: 32, borderRadius: radius.chip, alignItems: 'center', justifyContent: 'center' },
  text: { flex: 1, gap: 2 },
  title: { ...(typeScale.section as TextStyle), color: color.text },
  body: { ...(typeScale.caption as TextStyle), fontSize: 13, color: color.text2 },
  time: { ...(typeScale.caption as TextStyle), color: color.text3 },
});
