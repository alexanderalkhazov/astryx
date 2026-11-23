import { useState } from 'react';

import { Dialog } from '@/components/Dialog';
import { Text, type TextProps } from '@/components/Text';

import { GLOSSARY, type GlossaryKey } from './glossary';

/**
 * A jargon word the user can tap to have explained (T-161). Shown with a dotted underline in Simple mode; in Pro mode
 * pass <c>plain</c> to render it as ordinary text.
 */
export function Term({
  term,
  children,
  plain,
  ...text
}: { term: GlossaryKey; children?: string; plain?: boolean } & Omit<TextProps, 'children'>) {
  const [open, setOpen] = useState(false);
  const entry = GLOSSARY[term];
  const label = children ?? entry.term;
  if (plain) return <Text {...text}>{label}</Text>;
  return (
    <>
      <Text
        {...text}
        accessibilityRole="button"
        accessibilityHint={`Explains ${entry.term}`}
        onPress={() => setOpen(true)}
        style={[{ textDecorationLine: 'underline', textDecorationStyle: 'dotted' }, text.style]}
        testID={`term-${term}`}
      >
        {label}
      </Text>
      <Dialog
        visible={open}
        title={entry.term}
        body={entry.body}
        cancelLabel="Close"
        confirmLabel="Got it"
        onCancel={() => setOpen(false)}
        onConfirm={() => setOpen(false)}
      />
    </>
  );
}
