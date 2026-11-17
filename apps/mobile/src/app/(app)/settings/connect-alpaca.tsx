import { zodResolver } from '@hookform/resolvers/zod';
import { space } from '@astryx/design-tokens';
import { useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';
import { z } from 'zod';

import { Button } from '@/components/Button';
import { FormField } from '@/components/FormField';
import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { broker } from '@/lib/api';
import { applyServerErrors } from '@/lib/forms';
import { toast } from '@/state/toasts';

const schema = z.object({
  keyId: z.string().trim().min(10, 'Paste the API key ID from your Alpaca paper dashboard.'),
  secretKey: z.string().trim().min(20, 'Paste the secret shown when you generated the key.'),
});
type Values = z.infer<typeof schema>;

// PROPOSED screen (Settings › Broker connection, SYSTEM_DESIGN.md §17.6, T-141). Paper keys only: live trading is disabled.
export default function ConnectAlpacaScreen() {
  const queryClient = useQueryClient();
  const [failure, setFailure] = useState<string | null>(null);
  const { control, handleSubmit, setError, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { keyId: '', secretKey: '' },
  });

  const submit = handleSubmit(async (values) => {
    setFailure(null);
    try {
      const { data } = await broker.connect(values);
      await queryClient.invalidateQueries(); // the active portfolio is now the Alpaca one: every money view refetches
      toast({ tone: 'success', title: 'Alpaca connected', body: `Paper account ${data.accountRef ?? ''} · key ••••${data.keySuffix}` });
      router.back();
    } catch (error) {
      setFailure(applyServerErrors(error, setError, { keyId: 'keyId', secretKey: 'secretKey' }));
    }
  });

  return (
    <Screen header={<StackHeader title="Connect Alpaca" />}>
      <View style={styles.form}>
        <Text.Body tone="secondary">
          Use your Alpaca paper trading keys (Alpaca dashboard › Paper trading › API keys). Approved trades are sent to that account; Astryx
          never moves money in or out.
        </Text.Body>
        {failure ? <StatusBanner tone="error" title="Couldn't connect Alpaca" body={failure} testID="connect-alpaca-error" /> : null}
        <FormField
          control={control}
          name="keyId"
          label="API key ID"
          autoCapitalize="characters"
          autoCorrect={false}
          testID="alpaca-key-id"
        />
        <FormField control={control} name="secretKey" label="Secret key" secureTextEntry autoCorrect={false} testID="alpaca-secret" />
        <Text.Caption tone="tertiary">
          The secret is sent once over TLS, encrypted on the server, and never stored on this device.
        </Text.Caption>
        <Button
          label="Connect paper account"
          onPress={() => void submit()}
          loading={formState.isSubmitting}
          testID="connect-alpaca-submit"
        />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({ form: { gap: space[4], padding: space[4] } });
