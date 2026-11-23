import { color, radius, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Screen, ScreenHeader } from '@/components/Shell';
import { Text } from '@/components/Text';
import { GLOSSARY, type GlossaryKey } from '@/features/plain/glossary';

/** The order a beginner meets these words: the trade itself first, then what the screens show. */
const BASICS: GlossaryKey[] = ['paper', 'buyStop', 'stop', 'target', 'rewardRisk', 'positionSize', 'riskPerTrade'];

// T-194: Simple mode's Learn tab. The full learning path is T-186; for now the plain-words glossary and the guided trade.
export default function LearnScreen() {
  const [open, setOpen] = useState<GlossaryKey>();
  const rest = (Object.keys(GLOSSARY) as GlossaryKey[]).filter((k) => !BASICS.includes(k));

  const item = (key: GlossaryKey) => {
    const expanded = open === key;
    return (
      <Pressable
        key={key}
        accessibilityRole="button"
        accessibilityState={{ expanded }}
        onPress={() => setOpen(expanded ? undefined : key)}
        style={({ pressed }) => [styles.item, { opacity: pressed ? 0.8 : 1 }]}
        testID={`learn-${key}`}
      >
        <Text.Section>{GLOSSARY[key].term}</Text.Section>
        {expanded ? <Text.Body tone="secondary">{GLOSSARY[key].body}</Text.Body> : null}
      </Pressable>
    );
  };

  return (
    <Screen header={<ScreenHeader title="Learn" />}>
      <View style={styles.content} testID="learn">
        <Text.Body tone="secondary">Short, plain explanations of the words Astryx uses. Tap one to read it.</Text.Body>
        <View style={styles.card}>
          <Text.Section>Learn by doing</Text.Section>
          <Text.Body tone="secondary">The guided trade explains each step on a real idea, with pretend money.</Text.Body>
          <Button variant="secondary" label="Start the guided trade" onPress={() => router.push('/guided-trade')} />
        </View>
        <Text.Overline tone="tertiary">THE BASICS</Text.Overline>
        {BASICS.map(item)}
        <Text.Overline tone="tertiary">WHAT THE SCREENS SHOW</Text.Overline>
        {rest.map(item)}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[3], paddingBottom: space[6] },
  card: { gap: space[2], padding: space[4], borderRadius: radius.card, backgroundColor: color.surface1 },
  item: { gap: space[2], paddingVertical: space[3], borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: color.line },
});
