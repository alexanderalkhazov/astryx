import { chart, color, font, motion, space } from '@astryx/design-tokens';
import { Canvas, DashPathEffect, Group, Line, Path, Skia, type SkPath, vec } from '@shopify/react-native-skia';
import * as Haptics from 'expo-haptics';
import { useCallback, useMemo, useRef, useState } from 'react';
import { type LayoutChangeEvent, StyleSheet, Text, View } from 'react-native';
import { GestureDetector, usePanGesture } from 'react-native-gesture-handler';
import { useDerivedValue, useSharedValue } from 'react-native-reanimated';
import { scheduleOnRN } from 'react-native-worklets';

import { formatPrice, formatVolume } from '@/lib/format';

import { bodyWidth, gridRows, indexAt, type PriceBar, type PriceLevels, priceRange, sma, spreadTags, toPlotBars, xFor, yFor } from './geometry';

export type { PriceBar, PriceLevels } from './geometry';

export interface CandleOverlays {
  ma20?: boolean;
  ma50?: boolean;
  volume?: boolean;
  /** Entry, stop and target lines with gutter tags (the Stock screen shows them on 1M and 3M). */
  levels?: boolean;
}

export interface CandleChartProps {
  bars: readonly PriceBar[];
  /** Leading bars used only to warm up the moving averages; they are not drawn. */
  warmup?: number;
  overlays?: CandleOverlays;
  levels?: PriceLevels | null;
  /** The bar under the finger while scrubbing, then null on release (the readout returns to the last bar). */
  onScrub?: (bar: PriceBar | null) => void;
  /** "daily" bars read as dates in the accessibility summary; intraday bars as times. */
  intraday?: boolean;
}

const VOLUME_GAP = space[2];
const TAG_HEIGHT = 18;
const MA20 = color.series[1];
const MA50 = color.series[3];

/**
 * Candlestick chart (SYSTEM_DESIGN.md §8.4.3, §8.6; design 04): price pane 210 with MA20/MA50 and trade levels,
 * volume pane 40, 44 pt right gutter with level tags. A drag snaps to bars on the UI thread; the readout and a
 * selection haptic (at most every 30 ms) follow each new bar. Screen readers get a summary and the last 10 bars.
 */
export function CandleChart({ bars, warmup = 0, overlays = {}, levels, onScrub, intraday = false }: CandleChartProps) {
  const [width, setWidth] = useState(0);
  const plotWidth = Math.max(0, width - chart.axisGutter);
  const showVolume = overlays.volume ?? true;
  const showLevels = (overlays.levels ?? true) && !!levels;
  const height = chart.pricePane + (showVolume ? VOLUME_GAP + chart.volumePane : 0);

  const visible = useMemo(() => bars.slice(warmup), [bars, warmup]);
  const geometry = useMemo(() => {
    const all = toPlotBars(bars);
    const plot = all.slice(warmup);
    const closes = all.map((b) => b.c);
    const ma20 = overlays.ma20 ?? true ? sma(closes, 20).slice(warmup) : null;
    const ma50 = overlays.ma50 ?? true ? sma(closes, 50).slice(warmup) : null;
    const levelValues = showLevels && levels ? { entry: Number(levels.entry), stop: Number(levels.stop), target: Number(levels.target) } : null;
    const range = priceRange(plot, [...(ma20 ?? []), ...(ma50 ?? []), ...(levelValues ? Object.values(levelValues) : [])]);
    return { plot, ma20, ma50, levelValues, range, maxVolume: Math.max(1, ...plot.map((b) => b.v)) };
  }, [bars, warmup, overlays.ma20, overlays.ma50, showLevels, levels]);

  const paths = useMemo(() => {
    const { plot, ma20, ma50, range, maxVolume } = geometry;
    const count = plot.length;
    const upBodies = Skia.PathBuilder.Make();
    const downBodies = Skia.PathBuilder.Make();
    const upWicks = Skia.PathBuilder.Make();
    const downWicks = Skia.PathBuilder.Make();
    const upVolume = Skia.PathBuilder.Make();
    const downVolume = Skia.PathBuilder.Make();
    const done = (ma20Path: SkPath | null = null, ma50Path: SkPath | null = null) => ({
      upBodies: upBodies.build(), downBodies: downBodies.build(), upWicks: upWicks.build(), downWicks: downWicks.build(),
      upVolume: upVolume.build(), downVolume: downVolume.build(), ma20: ma20Path, ma50: ma50Path,
    });
    if (plotWidth <= 0 || count === 0) return done();

    const body = bodyWidth(count, plotWidth);
    const volumeTop = chart.pricePane + VOLUME_GAP;
    plot.forEach((b, i) => {
      const x = xFor(i, count, plotWidth);
      const top = yFor(Math.max(b.o, b.c), range, chart.pricePane);
      const bottom = yFor(Math.min(b.o, b.c), range, chart.pricePane);
      const wicks = b.up ? upWicks : downWicks;
      wicks.moveTo(x, yFor(b.h, range, chart.pricePane));
      wicks.lineTo(x, yFor(b.l, range, chart.pricePane));
      (b.up ? upBodies : downBodies).addRect(Skia.XYWHRect(x - body / 2, top, body, Math.max(1, bottom - top)));
      const barHeight = Math.max(1, (b.v / maxVolume) * chart.volumePane);
      (b.up ? upVolume : downVolume).addRect(Skia.XYWHRect(x - body / 2, volumeTop + chart.volumePane - barHeight, body, barHeight));
    });

    const line = (values: (number | null)[] | null) => {
      if (!values) return null;
      const path = Skia.PathBuilder.Make();
      let started = false;
      values.forEach((v, i) => {
        if (v === null) return;
        const x = xFor(i, count, plotWidth);
        const y = yFor(v, range, chart.pricePane);
        if (started) path.lineTo(x, y);
        else path.moveTo(x, y);
        started = true;
      });
      return path.build();
    };
    return done(line(ma20), line(ma50));
  }, [geometry, plotWidth]);

  // Scrub: the index lives on the UI thread; JS hears about each new bar for the readout, the tag and the haptic.
  const scrubIndex = useSharedValue(-1);
  const [scrubbed, setScrubbed] = useState<number | null>(null);
  const lastHaptic = useRef(0);
  const count = visible.length;

  const emit = useCallback(
    (index: number) => {
      if (index < 0) {
        setScrubbed(null);
        onScrub?.(null);
        return;
      }
      setScrubbed(index);
      onScrub?.(visible[index] ?? null);
      const now = Date.now();
      if (now - lastHaptic.current >= motion.scrubHapticThrottle) {
        lastHaptic.current = now;
        void Haptics.selectionAsync();
      }
    },
    [onScrub, visible],
  );

  const pan = usePanGesture({
    // A horizontal drag scrubs; a vertical one fails over to the surrounding ScrollView.
    activeOffsetX: [-4, 4],
    failOffsetY: [-12, 12],
    onBegin: (e) => {
      'worklet';
      if (e.x > plotWidth) return;
      const index = indexAt(e.x, count, plotWidth);
      scrubIndex.value = index;
      scheduleOnRN(emit, index);
    },
    onUpdate: (e) => {
      'worklet';
      const index = indexAt(Math.min(e.x, plotWidth), count, plotWidth);
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

  const crosshairX = useDerivedValue(() => (scrubIndex.value < 0 || count === 0 ? -10 : (plotWidth / count) * (scrubIndex.value + 0.5)));
  const crosshairTop = useDerivedValue(() => vec(crosshairX.value, 0));
  const crosshairBottom = useDerivedValue(() => vec(crosshairX.value, height));

  const tags = useMemo(() => {
    const { levelValues, range } = geometry;
    if (!levelValues || !levels) return [];
    const items = [
      { key: 'target', label: levels.target, y: yFor(levelValues.target, range, chart.pricePane), bg: color.gain },
      { key: 'entry', label: levels.entry, y: yFor(levelValues.entry, range, chart.pricePane), bg: color.text },
      { key: 'stop', label: levels.stop, y: yFor(levelValues.stop, range, chart.pricePane), bg: color.loss },
    ];
    const centres = spreadTags(items.map((t) => t.y), TAG_HEIGHT + 2, chart.pricePane);
    return items.map((t, i) => ({ ...t, line: t.y, y: centres[i]! }));
  }, [geometry, levels]);

  const scrubBar = scrubbed === null ? null : visible[scrubbed];
  const scrubTagY = scrubBar ? yFor(Number(scrubBar.c), geometry.range, chart.pricePane) : 0;

  const onLayout = (e: LayoutChangeEvent) => setWidth(Math.round(e.nativeEvent.layout.width));
  const last = visible.at(-1);
  const summary = last ? summarize(visible, intraday) : 'Price chart, no data';

  return (
    <View>
      <GestureDetector gesture={pan}>
        <View onLayout={onLayout} style={{ height }} accessible accessibilityRole="image" accessibilityLabel={summary} testID="candle-chart">
          {width > 0 ? (
            <Canvas style={{ width, height }}>
              {gridRows(chart.pricePane).map((y) => (
                <Line key={y} p1={vec(0, y)} p2={vec(plotWidth, y)} color={color.line} strokeWidth={1} />
              ))}
              {showVolume ? <Line p1={vec(0, chart.pricePane + VOLUME_GAP / 2)} p2={vec(plotWidth, chart.pricePane + VOLUME_GAP / 2)} color={color.line} strokeWidth={1} /> : null}
              {tags.map((t) => (
                <Line key={t.key} p1={vec(0, t.line)} p2={vec(plotWidth, t.line)} color={t.bg} strokeWidth={1} opacity={0.85}>
                  <DashPathEffect intervals={[4, 4]} />
                </Line>
              ))}
              {showVolume ? (
                <Group opacity={0.45}>
                  <Path path={paths.upVolume} color={color.gain} />
                  <Path path={paths.downVolume} color={color.loss} />
                </Group>
              ) : null}
              <Path path={paths.upWicks} color={color.gain} style="stroke" strokeWidth={1} />
              <Path path={paths.downWicks} color={color.loss} style="stroke" strokeWidth={1} />
              <Path path={paths.upBodies} color={color.gain} />
              <Path path={paths.downBodies} color={color.loss} />
              {paths.ma50 ? <Path path={paths.ma50} color={MA50} style="stroke" strokeWidth={1.5} strokeJoin="round" /> : null}
              {paths.ma20 ? <Path path={paths.ma20} color={MA20} style="stroke" strokeWidth={1.5} strokeJoin="round" /> : null}
              <Line p1={crosshairTop} p2={crosshairBottom} color={color.text2} strokeWidth={1} />
            </Canvas>
          ) : null}
          {tags.map((t) => (
            <GutterTag key={t.key} y={t.y} label={formatPrice(t.label)} background={t.bg} />
          ))}
          {scrubBar ? <GutterTag y={scrubTagY} label={formatPrice(scrubBar.c)} background={color.surface2} foreground={color.text} /> : null}
        </View>
      </GestureDetector>
      <LastBars bars={visible} intraday={intraday} />
    </View>
  );
}

function GutterTag({ y, label, background, foreground = color.bg }: { y: number; label: string; background: string; foreground?: string }) {
  return (
    <View pointerEvents="none" importantForAccessibility="no-hide-descendants" accessibilityElementsHidden style={[styles.tag, { top: y - TAG_HEIGHT / 2, backgroundColor: background }]}>
      <Text style={[styles.tagText, { color: foreground }]} numberOfLines={1} adjustsFontSizeToFit>
        {label}
      </Text>
    </View>
  );
}

const when = (bar: PriceBar, intraday: boolean) =>
  intraday ? new Date(bar.t).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit', timeZone: 'America/New_York' }) : formatDay(bar.d);

const formatDay = (isoDate: string) => new Date(`${isoDate}T12:00:00Z`).toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' });

/** "Price chart, 63 bars from Aug 8 to Nov 9. Last close 186.40. Range 152.10 to 186.77." */
export function summarize(bars: readonly PriceBar[], intraday = false): string {
  const first = bars[0]!;
  const last = bars.at(-1)!;
  let high = bars[0]!.h;
  let low = bars[0]!.l;
  for (const b of bars) {
    if (Number(b.h) > Number(high)) high = b.h;
    if (Number(b.l) < Number(low)) low = b.l;
  }
  return `Price chart, ${bars.length} bars from ${when(first, intraday)} to ${when(last, intraday)}. Last close ${formatPrice(last.c)}. Range ${formatPrice(low)} to ${formatPrice(high)}.`;
}

/** The last 10 bars as text for screen readers; visually collapsed (SYSTEM_DESIGN.md §8.13). */
function LastBars({ bars, intraday }: { bars: readonly PriceBar[]; intraday: boolean }) {
  const recent = bars.slice(-10).reverse();
  if (recent.length === 0) return null;
  return (
    <View style={styles.offscreen} accessibilityLabel="Last 10 bars" accessibilityRole="list" testID="candle-chart-last-bars">
      {recent.map((b) => (
        <Text key={b.t} accessible>
          {`${when(b, intraday)}: open ${formatPrice(b.o)}, high ${formatPrice(b.h)}, low ${formatPrice(b.l)}, close ${formatPrice(b.c)}, volume ${formatVolume(b.v)}`}
        </Text>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  tag: {
    position: 'absolute',
    right: 0,
    width: chart.axisGutter - 2,
    height: TAG_HEIGHT,
    borderRadius: 4,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 2,
  },
  tagText: { fontFamily: font.mono.semibold, fontSize: 10 },
  offscreen: { position: 'absolute', width: 1, height: 1, overflow: 'hidden', opacity: 0 },
});
