import { color, motion } from '@astryx/design-tokens';
import { useEffect } from 'react';
import { Pressable, StyleSheet } from 'react-native';
import Animated, { Easing, ReduceMotion, useAnimatedStyle, useSharedValue, withTiming } from 'react-native-reanimated';

export interface SwitchProps {
  value: boolean;
  onValueChange: (value: boolean) => void;
  accessibilityLabel: string;
  disabled?: boolean;
}

const TRACK_W = 44;
const KNOB = 20;
const PAD = 3;

/** On/off switch (design 03): signal-blue track when on, grey when off. */
export function Switch({ value, onValueChange, accessibilityLabel, disabled = false }: SwitchProps) {
  const progress = useSharedValue(value ? 1 : 0);
  useEffect(() => {
    progress.value = withTiming(value ? 1 : 0, {
      duration: motion.tab,
      easing: Easing.bezier(...motion.easing),
      reduceMotion: ReduceMotion.System,
    });
  }, [value, progress]);

  const knob = useAnimatedStyle(() => ({ transform: [{ translateX: progress.value * (TRACK_W - KNOB - PAD * 2) }] }));

  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ checked: value, disabled }}
      disabled={disabled}
      hitSlop={8}
      onPress={() => onValueChange(!value)}
      style={[styles.track, { backgroundColor: value ? color.signal : color.lineStrong, opacity: disabled ? 0.4 : 1 }]}
    >
      <Animated.View style={[styles.knob, { backgroundColor: value ? color.bg : color.text2 }, knob]} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  track: { width: TRACK_W, height: KNOB + PAD * 2, borderRadius: (KNOB + PAD * 2) / 2, padding: PAD, justifyContent: 'center' },
  knob: { width: KNOB, height: KNOB, borderRadius: KNOB / 2 },
});
