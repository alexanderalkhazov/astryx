import { color, size, space, type as typeScale } from '@astryx/design-tokens';
import { type Tabs, useRouter } from 'expo-router';
import { Activity, ChartPie, ChevronLeft, FlaskConical, House, type LucideIcon, ScanSearch } from 'lucide-react-native';
import type { ComponentProps, ReactNode } from 'react';
import { Pressable, StyleSheet, Text, type TextStyle, View } from 'react-native';

/** Props Expo Router 58 passes to a custom tab bar (its vendored BottomTabBarProps: state, descriptors, emitter, navigateToTab, insets). */
export type TabBarProps = Parameters<NonNullable<ComponentProps<typeof Tabs>['tabBar']>>[0];

const TAB_ICONS: Record<string, LucideIcon> = {
  index: House,
  discover: ScanSearch,
  portfolio: ChartPie,
  strategies: FlaskConical,
  activity: Activity,
};

/**
 * Five-tab bar (design 03 · Navigation): the active tab is shown by colour only, never a pill; Discover can carry a
 * count badge. Height 56 plus the home-indicator inset.
 */
export function TabBar({ state, descriptors, emitter, navigateToTab, insets }: TabBarProps) {
  return (
    <View style={[styles.tabBar, { paddingBottom: Math.max(insets.bottom, space[2]) }]} accessibilityRole="tablist">
      {state.routes.map((route, index) => {
        const { options } = descriptors[route.key]!;
        const focused = state.index === index;
        const Icon = TAB_ICONS[route.name] ?? House;
        const label = typeof options.title === 'string' ? options.title : route.name;
        const badge = options.tabBarBadge;
        const tint = focused ? color.text : color.text3;
        return (
          <Pressable
            key={route.key}
            accessibilityRole="tab"
            accessibilityState={{ selected: focused }}
            accessibilityLabel={badge === undefined ? label : `${label}, ${badge}`}
            testID={`tab-${route.name}`}
            onPress={() => {
              const event = emitter.emit({ type: 'tabPress', target: route.key, canPreventDefault: true });
              if (!focused && !event.defaultPrevented) navigateToTab(route.key);
            }}
            onLongPress={() => emitter.emit({ type: 'tabLongPress', target: route.key })}
            style={({ pressed }) => [styles.tab, { opacity: pressed ? 0.7 : 1 }]}
          >
            <View>
              <Icon size={22} strokeWidth={1.7} color={tint} />
              {badge !== undefined ? (
                <View style={styles.badge}>
                  <Text style={styles.badgeText}>{badge}</Text>
                </View>
              ) : null}
            </View>
            <Text style={[styles.tabLabel, { color: tint }]}>{label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

export interface StackHeaderProps {
  title: string;
  subtitle?: string;
  /** Defaults to router.back(). */
  onBack?: () => void;
  right?: ReactNode;
}

/** Back header with a centred title (design 03 · StackHeader). */
export function StackHeader({ title, subtitle, onBack, right }: StackHeaderProps) {
  const router = useRouter();
  return (
    <View style={styles.header}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel="Back"
        onPress={onBack ?? (() => router.back())}
        hitSlop={8}
        style={({ pressed }) => [styles.side, { opacity: pressed ? 0.6 : 1 }]}
      >
        <ChevronLeft size={24} strokeWidth={1.7} color={color.text} />
      </Pressable>
      <View style={styles.center}>
        <Text style={styles.title} numberOfLines={1} accessibilityRole="header">
          {title}
        </Text>
        {subtitle ? (
          <Text style={styles.subtitle} numberOfLines={1}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      <View style={[styles.side, styles.right]}>{right}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  tabBar: { flexDirection: 'row', backgroundColor: color.bar, borderTopWidth: 1, borderTopColor: color.line, paddingTop: space[2] },
  tab: { flex: 1, alignItems: 'center', gap: 4, minHeight: size.tabBar - space[2] },
  tabLabel: { ...(typeScale.caption as TextStyle), fontSize: 11, fontFamily: 'Geist_500Medium' },
  badge: {
    position: 'absolute',
    top: -6,
    right: -10,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    paddingHorizontal: 4,
    backgroundColor: color.signal,
    alignItems: 'center',
    justifyContent: 'center',
  },
  badgeText: { ...(typeScale.overline as TextStyle), fontSize: 10, letterSpacing: 0, color: color.bg },
  header: {
    minHeight: 52,
    flexDirection: 'row',
    alignItems: 'center',
    borderBottomWidth: 1,
    borderBottomColor: color.line,
    paddingHorizontal: space[2],
  },
  side: { width: size.touch, height: size.touch, alignItems: 'center', justifyContent: 'center' },
  right: { alignItems: 'flex-end' },
  center: { flex: 1, alignItems: 'center' },
  title: { ...(typeScale.section as TextStyle), color: color.text },
  subtitle: { ...(typeScale.caption as TextStyle), color: color.text2 },
});
