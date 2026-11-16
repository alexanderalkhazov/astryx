import { space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { X } from 'lucide-react-native';
import type { ReactNode } from 'react';
import { View } from 'react-native';

import { IconButton } from './Button';
import { StackHeader } from './Navigation';
import { Screen, ScreenHeader } from './Shell';
import { Text } from './Text';

type Props = {
  title: string;
  /** Design artboard(s) or "PROPOSED" — see SYSTEM_DESIGN.md §8.2. */
  design: string;
  section: string;
  /** Tab root (large title), pushed screen (back header, default) or full-screen modal (close button). */
  presentation?: 'tab' | 'push' | 'modal';
  /** Next to a tab title, e.g. the PAPER chip on Portfolio. */
  chip?: ReactNode;
  children?: ReactNode;
};

/** Temporary scaffold for routes whose screens are not implemented yet; navigation around it is real. */
export function PlaceholderScreen({ title, design, section, presentation = 'push', chip, children }: Props) {
  const header =
    presentation === 'push' ? (
      <StackHeader title={title} />
    ) : presentation === 'modal' ? (
      <View style={{ paddingHorizontal: space[4] }}>
        <ScreenHeader
          title={title}
          actions={<IconButton icon={X} variant="plain" accessibilityLabel="Close" onPress={() => router.back()} />}
        />
      </View>
    ) : undefined;

  return (
    <Screen header={header}>
      {presentation === 'tab' ? <ScreenHeader title={title} chip={chip} /> : null}
      <Text.Overline tone="pending">Not implemented · {design}</Text.Overline>
      <Text.Caption tone="tertiary">Spec: SYSTEM_DESIGN.md {section}</Text.Caption>
      {children ? <View style={{ marginTop: space[5], gap: space[3] }}>{children}</View> : null}
    </Screen>
  );
}
