import { color, motion } from '@astryx/design-tokens';
import { Canvas, DashPathEffect, Line, Path, Skia, type SkPath, vec } from '@shopify/react-native-skia';
import * as Haptics from 'expo-haptics';
import { useCallback, useMemo, useRef, useState } from 'react';
import { type LayoutChangeEvent, View } from 'react-native';
import { GestureDetector, usePanGesture } from 'react-native-gesture-handler';
import { useDerivedValue, useSharedValue } from 'react-native-reanimated';
import { scheduleOnRN } from 'react-native-worklets';

import { gridRows, indexAt, priceRange, xFor, yFor } from './geometry';

/** One point of a series: an instant and a decimal-string value. */
export interface LinePoint {
  t: string;
  v: string;
}

export interface LineSeries {
  key: string;
  points: readonly LinePoint[];
  /** Defaults to the design's series palette in order. */
  color?: string;
}

export interface LineChartProps {
  series: readonly LineSeries[];
  /** Grey dashed overlay, e.g. "vs S&P 500" (design 01). Same length and instants as the series. */
  benchmark?: readonly LinePoint[];
  height?: number;
  /** Index of the point under the finger while scrubbing, then null on release. */
  onScrub?: (index: number | null) => void;
  accessibilityLabel: string;
}

/**
 * Line chart (SYSTEM_DESIGN.md §8.6): one or more series plus an optional dashed benchmark, scrubbed like
 * CandleChart (snap to a point, selection haptic at most every 30 ms, release returns to the latest point).
 */
export function LineChart({ series, benchmark, height = 160, onScrub, accessibilityLabel }: LineChartProps) {
  const [width, setWidth] = useState(0);
  const count = Math.max(0, ...series.map((s) => s.points.length));

  const paths = useMemo(() => {
    const numeric = series.map((s) => s.points.map((p) => Number(p.v)));
    const bench = benchmark?.map((p) => Number(p.v)) ?? [];
    const range = priceRange([], [...numeric.flat(), ...bench], 0.08);
    const line = (values: number[]): SkPath => {
      const builder = Skia.PathBuilder.Make();
      values.forEach((v, i) => {
        const x = xFor(i, count, width);
        const y = yFor(v, range, height);
        if (i === 0) builder.moveTo(x, y);
        else builder.lineTo(x, y);
      });
      return builder.build();
    };
    return {
      series: numeric.map((values, i) => ({ key: series[i]!.key, path: line(values), color: series[i]!.color ?? color.series[i % color.series.length]! })),
      benchmark: bench.length > 0 ? line(bench) : null,
    };
  }, [series, benchmark, count, width, height]);

  const scrubIndex = useSharedValue(-1);
  const lastHaptic = useRef(0);
  const emit = useCallback(
    (index: number) => {
      onScrub?.(index < 0 ? null : index);
      if (index < 0) return;
      const now = Date.now();
      if (now - lastHaptic.current >= motion.scrubHapticThrottle) {
        lastHaptic.current = now;
        void Haptics.selectionAsync();
      }
    },
    [onScrub],
  );

  const pan = usePanGesture({
    activeOffsetX: [-4, 4],
    failOffsetY: [-12, 12],
    onBegin: (e) => {
      'worklet';
      const index = indexAt(e.x, count, width);
      scrubIndex.value = index;
      scheduleOnRN(emit, index);
    },
    onUpdate: (e) => {
      'worklet';
      const index = indexAt(e.x, count, width);
      if (index !== scrubIndex.value) {
        scrubIndex.value = index;
        scheduleOnRN(emit, index);
      }
    },
    onFinalize: () => {
      'worklet';
      if (scrubIndex.value === -1) return;
      scrubIndex.value = -1;
      scheduleOnRN(emit, -1);
    },
  });

  const crosshairX = useDerivedValue(() => (scrubIndex.value < 0 || count === 0 ? -10 : (width / count) * (scrubIndex.value + 0.5)));
  const top = useDerivedValue(() => vec(crosshairX.value, 0));
  const bottom = useDerivedValue(() => vec(crosshairX.value, height));

  const onLayout = (e: LayoutChangeEvent) => setWidth(Math.round(e.nativeEvent.layout.width));

  return (
    <GestureDetector gesture={pan}>
      <View onLayout={onLayout} style={{ height }} accessible accessibilityRole="image" accessibilityLabel={accessibilityLabel} testID="line-chart">
        {width > 0 && count > 0 ? (
          <Canvas style={{ width, height }}>
            {gridRows(height, 3).map((y) => (
              <Line key={y} p1={vec(0, y)} p2={vec(width, y)} color={color.line} strokeWidth={1} />
            ))}
            {paths.benchmark ? (
              <Path path={paths.benchmark} color={color.benchmark} style="stroke" strokeWidth={1.5}>
                <DashPathEffect intervals={[5, 4]} />
              </Path>
            ) : null}
            {paths.series.map((s) => (
              <Path key={s.key} path={s.path} color={s.color} style="stroke" strokeWidth={2} strokeJoin="round" strokeCap="round" />
            ))}
            <Line p1={top} p2={bottom} color={color.text2} strokeWidth={1} />
          </Canvas>
        ) : null}
      </View>
    </GestureDetector>
  );
}
