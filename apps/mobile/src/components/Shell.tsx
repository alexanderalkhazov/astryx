import { color, radius, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { Bell, Info, Search, SlidersHorizontal } from 'lucide-react-native';
import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { type Edge, SafeAreaView } from 'react-native-safe-area-context';

import { IconButton } from './Button';
import { Text } from './Text';

/** Widest content column: phones are 360–430 wide; anything wider centres the column (SYSTEM_DESIGN.md §8.12). */
export const MAX_CONTENT_WIDTH = 430;

export interface ScreenProps {
  children: ReactNode;
  /** Wrap in a ScrollView (default). Screens with their own list pass false. */
  scroll?: boolean;
  /** Tab screens sit above the tab bar, which handles the bottom inset itself. */
  edges?: Edge[];
  /** Fixed above the scrolling content, full column width (e.g. a StackHeader). */
  header?: ReactNode;
  /** Sticky bar below the content (e.g. the Stock screen's action bar); it takes the bottom safe area. */
  footer?: ReactNode;
}

/** Screen frame: background, top safe area, 16 pt gutter, centred column. */
export function Screen({ children, scroll = true, edges = ['top'], header, footer }: ScreenProps) {
  return (
    <SafeAreaView style={styles.safe} edges={edges}>
      {header ? <View style={styles.column}>{header}</View> : null}
      {scroll ? (
        // "handled": a tap on a row or button acts on the first try instead of only dismissing the keyboard.
        <ScrollView contentContainerStyle={[styles.column, styles.content]} keyboardShouldPersistTaps="handled">
          {children}
        </ScrollView>
      ) : (
        <View style={[styles.column, styles.content, styles.fill]}>{children}</View>
      )}
      {footer ? (
        <SafeAreaView edges={['bottom']} style={styles.footer}>
          <View style={styles.column}>{footer}</View>
        </SafeAreaView>
      ) : null}
    </SafeAreaView>
  );
}

export interface ScreenHeaderProps {
  title: string;
  /** Shown right after the title, e.g. the PAPER chip. */
  chip?: ReactNode;
  /** Icon buttons on the right. */
  actions?: ReactNode;
}

/** Large-title header of a tab screen (design 01 · Home: "Overview" + PAPER + icons). */
export function ScreenHeader({ title, chip, actions }: ScreenHeaderProps) {
  return (
    <View style={styles.header}>
      <View style={styles.titleRow}>
        <Text.Title accessibilityRole="header" numberOfLines={1} style={styles.shrink}>
          {title}
        </Text.Title>
        {chip}
      </View>
      {actions ? <View style={styles.actions}>{actions}</View> : null}
    </View>
  );
}

/**
 * PAPER chip (design safety rule 4): persistent on Home and Portfolio so simulated money is never mistaken for real.
 * Amber outline with a dot; mono uppercase.
 */
export function PaperChip() {
  return (
    <View style={styles.paper} accessible accessibilityRole="text" accessibilityLabel="Paper trading account" testID="paper-chip">
      <View style={styles.paperDot} />
      <Text.Overline tone="pending">PAPER</Text.Overline>
    </View>
  );
}

/**
 * "Illustrative sample data — not live prices" caption (SYSTEM_DESIGN.md §8.1): required wherever numbers are not from
 * a live feed. Optionally shows when the values were last updated, e.g. "14:32 ET".
 */
export function SampleDataNotice({ updatedAt }: { updatedAt?: string }) {
  return (
    <View style={styles.notice}>
      <View style={styles.noticeLabel}>
        <Info size={14} strokeWidth={1.7} color={color.text3} />
        <Text.Caption tone="tertiary" style={styles.shrink}>
          Illustrative sample data — not live prices
        </Text.Caption>
      </View>
      {updatedAt ? (
        <Text.DataSm tone="tertiary" style={styles.updated}>
          Updated {updatedAt}
        </Text.DataSm>
      ) : null}
    </View>
  );
}

/** Home header actions: search, alerts (dot when unread) and settings. */
export function HomeHeader({ unreadAlerts = false }: { unreadAlerts?: boolean }) {
  return (
    <ScreenHeader
      title="Overview"
      chip={<PaperChip />}
      actions={
        <>
          <IconButton icon={Search} variant="plain" accessibilityLabel="Search" testID="header-search" onPress={() => router.push('/search')} />
          <IconButton
            icon={Bell}
            variant="plain"
            testID="header-alerts"
            accessibilityLabel={unreadAlerts ? 'Alerts, unread' : 'Alerts'}
            onPress={() => router.navigate({ pathname: '/activity', params: { filter: 'alerts' } })}
            badge={unreadAlerts ? <View style={styles.alertDot} testID="alerts-unread" /> : undefined}
          />
          <IconButton icon={SlidersHorizontal} variant="plain" accessibilityLabel="Settings" testID="header-settings" onPress={() => router.push('/settings')} />
        </>
      }
    />
  );
}

const styles = StyleSheet.create({
  footer: { borderTopWidth: 1, borderTopColor: color.line, backgroundColor: color.bg },
  safe: { flex: 1, backgroundColor: color.bg },
  column: { width: '100%', maxWidth: MAX_CONTENT_WIDTH, alignSelf: 'center' },
  content: { paddingHorizontal: space[4], paddingBottom: space[6], gap: space[3] },
  fill: { flex: 1 },
  shrink: { flexShrink: 1 },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: 56, gap: space[2] },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: space[3], flexShrink: 1 },
  actions: { flexDirection: 'row', alignItems: 'center', marginRight: -space[2] },
  paper: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    height: 26,
    paddingHorizontal: 10,
    borderRadius: radius.chip,
    borderWidth: 1,
    borderColor: 'rgba(242,181,68,0.6)',
    backgroundColor: 'rgba(242,181,68,0.06)',
  },
  paperDot: { width: 6, height: 6, borderRadius: 3, backgroundColor: color.pending },
  notice: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: space[3] },
  noticeLabel: { flexDirection: 'row', alignItems: 'center', gap: 6, flexShrink: 1 },
  // Caption size, so the notice and the time share one line at 390 pt (design 01).
  updated: { fontSize: 12 },
  alertDot: { position: 'absolute', top: 9, right: 9, width: 8, height: 8, borderRadius: 2, backgroundColor: color.loss },
});
