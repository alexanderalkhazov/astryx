import { color } from '@astryx/design-tokens';
import { Canvas, Path, Skia } from '@shopify/react-native-skia';
import { useMemo } from 'react';
import { View } from 'react-native';

/**
 * Tiny trend line for list rows (design 02): closes as decimal strings, drawn in gain or loss colour by whether the
 * last close is above the first. Numbers are for pixel positions only; decorative to screen readers.
 */
export function Sparkline({ closes, width = 112, height = 44 }: { closes: readonly string[]; width?: number; height?: number }) {
  const { path, up } = useMemo(() => {
    const values = closes.map(Number);
    const builder = Skia.PathBuilder.Make();
    if (values.length < 2) return { path: builder.build(), up: true };
    const min = Math.min(...values);
    const span = Math.max(...values) - min || 1;
    values.forEach((v, i) => {
      const x = (i / (values.length - 1)) * width;
      const y = height - 2 - ((v - min) / span) * (height - 4);
      if (i === 0) builder.moveTo(x, y);
      else builder.lineTo(x, y);
    });
    return { path: builder.build(), up: values[values.length - 1]! >= values[0]! };
  }, [closes, width, height]);

  return (
    <View accessibilityElementsHidden importantForAccessibility="no-hide-descendants" style={{ width, height }}>
      <Canvas style={{ width, height }}>
        <Path path={path} color={up ? color.gain : color.loss} style="stroke" strokeWidth={1.5} strokeJoin="round" strokeCap="round" />
      </Canvas>
    </View>
  );
}
