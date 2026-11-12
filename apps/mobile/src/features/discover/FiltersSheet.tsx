import { space } from '@astryx/design-tokens';
import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Chip } from '@/components/Chip';
import { Sheet } from '@/components/Sheet';
import { Text } from '@/components/Text';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';

import { DEFAULT_FILTERS, type DiscoverFilters, HORIZONS, MARKET_CAPS, MIN_SCORES, SECTORS, SORTS } from './filters';
import { useSignalCount } from './useSignals';

/**
 * Filter scanner (design 03): sector, market cap, timeframe, minimum score and sort as single-choice chip groups.
 * Changes are a draft with a live "Show N setups" count (D3); applying sets the screen's filters.
 */
export function FiltersSheet({ open, filters, onApply, onClose }: { open: boolean; filters: DiscoverFilters; onApply: (f: DiscoverFilters) => void; onClose: () => void }) {
  const [draft, setDraft] = useState(filters);
  useEffect(() => {
    if (open) setDraft(filters);
  }, [open, filters]);
  const debounced = useDebouncedValue(draft, 150);
  const count = useSignalCount(debounced, open);
  const patch = (p: Partial<DiscoverFilters>) => setDraft((d) => ({ ...d, ...p }));

  const label = count.data === undefined ? 'Show setups' : `Show ${count.data} ${count.data === 1 ? 'setup' : 'setups'}`;

  return (
    <Sheet open={open} onClose={onClose} accessibilityLabel="Filter scanner">
      <View style={styles.header}>
        <Text.Headline>Filter scanner</Text.Headline>
        <Button variant="link" label="Reset" onPress={() => setDraft({ ...DEFAULT_FILTERS, type: draft.type })} />
      </View>

      <Group title="Sector">
        <Chip label="All" selected={!draft.sector} onPress={() => patch({ sector: undefined })} />
        {SECTORS.map((s) => (
          <Chip key={s.value} label={s.label} selected={draft.sector === s.value} onPress={() => patch({ sector: s.value })} />
        ))}
      </Group>
      <Group title="Market capitalization">
        <Chip label="Any" selected={!draft.marketCap} onPress={() => patch({ marketCap: undefined })} />
        {MARKET_CAPS.map((m) => (
          <Chip key={m.value} label={m.label} selected={draft.marketCap === m.value} onPress={() => patch({ marketCap: m.value })} />
        ))}
      </Group>
      <Group title="Timeframe">
        <Chip label="Any" selected={!draft.horizon} onPress={() => patch({ horizon: undefined })} />
        {HORIZONS.map((h) => (
          <Chip key={h.value} label={h.label} selected={draft.horizon === h.value} onPress={() => patch({ horizon: h.value })} />
        ))}
      </Group>
      <Group title="Minimum opportunity score">
        <Chip label="Any" selected={!draft.minScore} onPress={() => patch({ minScore: undefined })} />
        {MIN_SCORES.map((m) => (
          <Chip key={m} label={`${m}+`} selected={draft.minScore === m} onPress={() => patch({ minScore: m })} />
        ))}
      </Group>
      <Group title="Sort by">
        {SORTS.map((s) => (
          <Chip key={s.value} label={s.label} selected={draft.sort === s.value} onPress={() => patch({ sort: s.value })} />
        ))}
      </Group>

      <Button label={label} onPress={() => onApply(draft)} testID="filters-apply" />
    </Sheet>
  );
}

function Group({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <View style={styles.group} accessibilityRole="radiogroup" accessibilityLabel={title}>
      <Text.Body tone="secondary">{title}</Text.Body>
      <View style={styles.chips}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  group: { gap: space[2] },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space[2] },
});
