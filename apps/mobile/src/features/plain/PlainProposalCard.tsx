import type { Proposal, RiskAssessment } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { StyleSheet, View } from 'react-native';

import { Text } from '@/components/Text';

import { plainProposal } from './plainProposal';
import { Term } from './Term';

/** "In plain words": the proposal as a beginner would want it explained (T-161, Simple mode). */
export function PlainProposalCard({ proposal, assessment }: { proposal: Proposal; assessment: RiskAssessment }) {
  const plain = plainProposal(proposal, assessment);
  return (
    <View style={styles.card} testID="plain-proposal">
      <Text.Overline tone="signal">IN PLAIN WORDS</Text.Overline>
      <Text.Section>{plain.headline}</Text.Section>
      {plain.outcomes.map((line) => (
        <Text.Body key={line} tone="secondary">{`• ${line}`}</Text.Body>
      ))}
      <Text.Overline tone="tertiary" style={styles.heading}>
        WHY THIS
      </Text.Overline>
      {plain.why.map((line) => (
        <Text.Body key={line} tone="secondary">
          {line}
        </Text.Body>
      ))}
      <Text.Overline tone="tertiary" style={styles.heading}>
        WHAT COULD GO WRONG
      </Text.Overline>
      {plain.risks.map((line) => (
        <Text.Body key={line} tone="secondary">{`• ${line}`}</Text.Body>
      ))}
      <View style={styles.terms}>
        <Text.Caption tone="tertiary">Tap to learn: </Text.Caption>
        <Term term="stop" variant="caption" tone="secondary" />
        <Text.Caption tone="tertiary"> · </Text.Caption>
        <Term term="target" variant="caption" tone="secondary" />
        <Text.Caption tone="tertiary"> · </Text.Caption>
        <Term term="positionSize" variant="caption" tone="secondary" />
        {proposal.orderType === 'buy_stop' ? (
          <>
            <Text.Caption tone="tertiary"> · </Text.Caption>
            <Term term="buyStop" variant="caption" tone="secondary" />
          </>
        ) : null}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    gap: space[2],
    padding: space[4],
    borderRadius: radius.card,
    backgroundColor: color.surface1,
    borderWidth: 1,
    borderColor: color.line,
  },
  heading: { marginTop: space[2] },
  terms: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', marginTop: space[2] },
});
