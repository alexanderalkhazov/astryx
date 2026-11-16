import { color, motion, radius, space, type as typeScale } from '@astryx/design-tokens';
import { useEffect } from 'react';
import { ActivityIndicator, StyleSheet, Text, type TextStyle, View, type ViewStyle } from 'react-native';
import Animated, {
  Easing,
  ReduceMotion,
  useAnimatedStyle,
  useSharedValue,
  withDelay,
  withRepeat,
  withTiming,
} from 'react-native-reanimated';

import { Button } from './Button';

export interface SkeletonProps {
  width?: ViewStyle['width'];
  height?: number;
  /** Position in a list: each row starts 120 ms after the previous one (design: "staggered 120 ms"). */
  index?: number;
  style?: ViewStyle;
}

/** Pulsing placeholder (design: 1400 ms opacity pulse). Reduce Motion shows a static block. */
export function Skeleton({ width = '100%', height = 12, index = 0, style }: SkeletonProps) {
  const opacity = useSharedValue(0.45);
  useEffect(() => {
    opacity.value = withDelay(
      index * motion.skeletonStagger,
      withRepeat(
        withTiming(1, { duration: motion.skeleton / 2, easing: Easing.inOut(Easing.ease), reduceMotion: ReduceMotion.System }),
        -1,
        true,
      ),
    );
  }, [index, opacity]);
  const animated = useAnimatedStyle(() => ({ opacity: opacity.value }));
  return (
    <Animated.View
      accessibilityElementsHidden
      importantForAccessibility="no"
      style={[styles.skeleton, { width, height }, animated, style]}
    />
  );
}

/** One skeleton list row in the shape of an opportunity row. */
export function SkeletonRow({ index = 0 }: { index?: number }) {
  return (
    <View style={styles.row} accessibilityLabel="Loading" accessible>
      <View style={styles.rowTop}>
        <Skeleton width={56} height={14} index={index} />
        <Skeleton width={72} height={14} index={index} />
      </View>
      <Skeleton width="60%" height={10} index={index} />
      <Skeleton width="90%" height={10} index={index} />
    </View>
  );
}

/** Inline spinner for small actions (design: "Spinner · inline actions only"). */
export function Spinner({ label = 'Loading' }: { label?: string }) {
  return <ActivityIndicator accessibilityLabel={label} color={color.text2} size="small" />;
}

export interface EmptyStateProps {
  title: string;
  /** Says why it's empty. */
  body: string;
  /** Exactly one way out (design: "then offer one clear way out"). */
  action?: { label: string; onPress: () => void };
}

export function EmptyState({ title, body, action }: EmptyStateProps) {
  return (
    <View style={styles.empty}>
      <Text style={styles.title} accessibilityRole="header">
        {title}
      </Text>
      <Text style={styles.body}>{body}</Text>
      {action ? (
        <Button label={action.label} onPress={action.onPress} block={false} style={{ marginTop: space[2], paddingHorizontal: space[5] }} />
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  skeleton: { backgroundColor: color.surface2, borderRadius: radius.badge },
  row: { gap: space[2], paddingVertical: space[3], borderBottomWidth: 1, borderBottomColor: color.line },
  rowTop: { flexDirection: 'row', justifyContent: 'space-between' },
  empty: {
    alignItems: 'center',
    gap: space[2],
    paddingVertical: space[6],
    paddingHorizontal: space[4],
    borderRadius: radius.card,
    borderWidth: 1,
    borderStyle: 'dashed',
    borderColor: color.lineStrong,
  },
  title: { ...(typeScale.section as TextStyle), color: color.text, textAlign: 'center' },
  body: { ...(typeScale.body as TextStyle), color: color.text2, textAlign: 'center' },
});
