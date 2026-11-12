import { chart, color, font, space } from '@astryx/design-tokens';
import { Canvas, DashPathEffect, Line, Path, Rect, Skia, type SkPath, vec } from '@shopify/react-native-skia';
import { type ReactNode, useMemo, useState } from 'react';
import { type LayoutChangeEvent, StyleSheet, Text as RNText, View } from 'react-native';

import { Text } from '@/components/Text';

import { type PriceBar, xFor } from './geometry';
import { macdLines, rsiLine } from './paneMath';

export const MACD_COLOR = color.series[2];
export const SIGNAL_COLOR = color.series[1];

export interface IndicatorPaneProps {
  kind: 'rsi' | 'macd';
  /** Same bars and warm-up as the CandleChart above, so the panes line up bar for bar. */
  bars: readonly PriceBar[];
  warmup?: number;
  /** Right-hand value(s) from the server snapshot (M7); the line itself is drawn from the bars. */
  readout: ReactNode;
  accessibilityLabel: string;
}

/**
 * RSI(14) or MACD(12,26,9) pane under the price chart (design 04/05, SYSTEM_DESIGN.md §8.4.3): 64 pt high, the same
 * 44 pt right gutter as the CandleChart. RSI shows dashed 70/30 guides over a band; MACD shows its histogram, the
 * MACD and signal lines and a zero line.
 */
export function IndicatorPane({ kind, bars, warmup = 0, readout, accessibilityLabel }: IndicatorPaneProps) {
  const [width, setWidth] = useState(0);
  const plotWidth = Math.max(0, width - chart.axisGutter);
  const height = chart.indicatorPane;

  const drawing = useMemo(() => {
    const closes = bars.map((b) => Number(b.c)); // pixel positioning only
    const count = bars.length - warmup;
    const x = (i: number) => xFor(i, count, plotWidth);
    const line = (values: (number | null)[], y: (v: number) => number): SkPath => {
      const builder = Skia.PathBuilder.Make();
      let started = false;
      values.slice(warmup).forEach((v, i) => {
        if (v === null) return;
        if (started) builder.lineTo(x(i), y(v));
        else builder.moveTo(x(i), y(v));
        started = true;
      });
      return builder.build();
    };

    if (kind === 'rsi') {
      const y = (v: number) => ((100 - v) / 100) * height;
      return { kind, rsi: line(rsiLine(closes), y), y70: y(70), y30: y(30) } as const;
    }

    const { macd, signal, hist } = macdLines(closes);
    const visible = [macd, signal, hist].flatMap((a) => a.slice(warmup)).filter((v): v is number => v !== null);
    const extent = Math.max(0.0001, ...visible.map(Math.abs)) * 1.1;
    const y = (v: number) => height / 2 - (v / extent) * (height / 2);
    const barWidth = Math.max(1, Math.min(6, (plotWidth / Math.max(count, 1)) * 0.6));
    const up = Skia.PathBuilder.Make();
    const down = Skia.PathBuilder.Make();
    hist.slice(warmup).forEach((v, i) => {
      if (v === null) return;
      const top = Math.min(y(v), y(0));
      (v >= 0 ? up : down).addRect(Skia.XYWHRect(x(i) - barWidth / 2, top, barWidth, Math.max(1, Math.abs(y(v) - y(0)))));
    });
    return { kind, macd: line(macd, y), signal: line(signal, y), histUp: up.build(), histDown: down.build(), zero: y(0) } as const;
  }, [bars, warmup, kind, plotWidth, height]);

  const onLayout = (e: LayoutChangeEvent) => setWidth(Math.round(e.nativeEvent.layout.width));

  return (
    <View style={styles.wrap}>
      <View style={styles.header}>
        <Text.Caption tone="secondary">{kind === 'rsi' ? 'RSI (14)' : 'MACD (12, 26, 9)'}</Text.Caption>
        {readout}
      </View>
      <View onLayout={onLayout} style={{ height }} accessible accessibilityRole="image" accessibilityLabel={accessibilityLabel} testID={`pane-${kind}`}>
        {width > 0 ? (
          <Canvas style={{ width, height }}>
            {drawing.kind === 'rsi' ? (
              <>
                <Rect x={0} y={drawing.y70} width={plotWidth} height={drawing.y30 - drawing.y70} color={color.surface1} />
                {[drawing.y70, drawing.y30].map((y) => (
                  <Line key={y} p1={vec(0, y)} p2={vec(plotWidth, y)} color={color.lineStrong} strokeWidth={1}>
                    <DashPathEffect intervals={[3, 3]} />
                  </Line>
                ))}
                <Path path={drawing.rsi} color={color.signal} style="stroke" strokeWidth={1.5} strokeJoin="round" />
              </>
            ) : (
              <>
                <Line p1={vec(0, drawing.zero)} p2={vec(plotWidth, drawing.zero)} color={color.line} strokeWidth={1} />
                <Path path={drawing.histUp} color={MACD_COLOR} opacity={0.35} />
                <Path path={drawing.histDown} color={SIGNAL_COLOR} opacity={0.35} />
                <Path path={drawing.macd} color={MACD_COLOR} style="stroke" strokeWidth={1.5} strokeJoin="round" />
                <Path path={drawing.signal} color={SIGNAL_COLOR} style="stroke" strokeWidth={1.5} strokeJoin="round" />
              </>
            )}
          </Canvas>
        ) : null}
        {drawing.kind === 'rsi' ? (
          <>
            <GutterLabel y={drawing.y70} label="70" />
            <GutterLabel y={drawing.y30} label="30" />
          </>
        ) : null}
      </View>
    </View>
  );
}

function GutterLabel({ y, label }: { y: number; label: string }) {
  return (
    <RNText style={[styles.gutter, { top: y - 7 }]} accessibilityElementsHidden importantForAccessibility="no">
      {label}
    </RNText>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: space[1] },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  gutter: { position: 'absolute', right: 0, width: chart.axisGutter - 4, textAlign: 'right', fontFamily: font.mono.medium, fontSize: 11, color: color.text3 },
});
