import { color, radius, space, type as typeScale } from '@astryx/design-tokens';
import * as Haptics from 'expo-haptics';
import { useEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, StyleSheet, Text, type TextStyle, View } from 'react-native';
import Animated, { cancelAnimation, Easing, useAnimatedStyle, useSharedValue, withTiming } from 'react-native-reanimated';

export const HOLD_MS = 1200;

export interface HoldToConfirmProps {
  label?: string;
  holdingLabel?: string;
  busyLabel?: string;
  /** Shows the busy label and ignores presses (e.g. while the order sends). */
  busy?: boolean;
  disabled?: boolean;
  /** The screen-reader path: the native confirm's title and body. */
  confirmTitle: string;
  confirmBody: string;
  onConfirm: () => void;
  durationMs?: number;
  testID?: string;
}

/**
 * Hold-to-approve (design 07, SYSTEM_DESIGN.md §8.4.5): a 56 pt control that fills linearly over 1200 ms while held.
 * Releasing early resets it; completing fires once with a success haptic. Screen readers get an "Approve" action
 * followed by a second, native confirmation, so approval is never one accidental tap away.
 */
export function HoldToConfirm({
  label = 'Hold to approve',
  holdingLabel = 'Keep holding…',
  busyLabel = 'Sending…',
  busy = false,
  disabled = false,
  confirmTitle,
  confirmBody,
  onConfirm,
  durationMs = HOLD_MS,
  testID,
}: HoldToConfirmProps) {
  const progress = useSharedValue(0);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const fired = useRef(false);
  const [holding, setHolding] = useState(false);
  const inert = busy || disabled;

  useEffect(() => () => clearTimeout(timer.current), []);
  useEffect(() => {
    if (!busy) fired.current = false; // a failed send unlocks for another attempt
  }, [busy]);

  const complete = () => {
    if (fired.current) return;
    fired.current = true;
    setHolding(false);
    void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    onConfirm();
  };

  const start = () => {
    if (inert || fired.current) return;
    setHolding(true);
    progress.value = withTiming(1, { duration: durationMs, easing: Easing.linear });
    timer.current = setTimeout(complete, durationMs);
  };

  const release = () => {
    clearTimeout(timer.current);
    if (fired.current) return;
    setHolding(false);
    cancelAnimation(progress);
    progress.value = withTiming(0, { duration: 150 });
  };

  const fill = useAnimatedStyle(() => ({ width: `${progress.value * 100}%` }));
  const text = busy ? busyLabel : holding ? holdingLabel : label;

  return (
    <Pressable
      onPressIn={start}
      onPressOut={release}
      disabled={inert}
      accessibilityRole="button"
      accessibilityLabel={text}
      accessibilityHint="Press and hold to approve. Screen reader users: use the Approve action."
      accessibilityState={{ disabled: inert, busy }}
      accessibilityActions={[{ name: 'activate', label: 'Approve' }]}
      onAccessibilityAction={(event) => {
        if (event.nativeEvent.actionName !== 'activate' || inert) return;
        Alert.alert(confirmTitle, confirmBody, [
          { text: 'Cancel', style: 'cancel' },
          { text: 'Approve', style: 'default', onPress: complete },
        ]);
      }}
      style={[styles.track, inert && !busy ? styles.disabled : null]}
      testID={testID}
    >
      <Animated.View style={[styles.fill, fill]} />
      <View style={styles.content}>
        {busy ? <ActivityIndicator color={color.bg} size="small" /> : null}
        <Text style={styles.label}>{text}</Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  track: { height: 56, borderRadius: radius.button, backgroundColor: color.surface2, overflow: 'hidden', justifyContent: 'center' },
  fill: { position: 'absolute', left: 0, top: 0, bottom: 0, backgroundColor: color.text },
  content: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: space[2] },
  label: { ...(typeScale.section as TextStyle), color: color.text, mixBlendMode: 'difference' },
  disabled: { opacity: 0.5 },
});
