import { zodResolver } from '@hookform/resolvers/zod';
import { space } from '@astryx/design-tokens';
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
import { settings } from '@/lib/api';
import { applyServerErrors, newPasswordField } from '@/lib/forms';
import { toast } from '@/state/toasts';

const schema = z
  .object({ current: z.string().min(1, 'Enter your current password.'), password: newPasswordField, confirm: z.string() })
  .refine((v) => v.password === v.confirm, { path: ['confirm'], message: "Passwords don't match." });
type Values = z.infer<typeof schema>;

// PROPOSED screen (Settings › Security, §11.3 A11). Other devices are signed out; this one stays signed in.
export default function ChangePasswordScreen() {
  const [failure, setFailure] = useState<string | null>(null);
  const { control, handleSubmit, setError, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { current: '', password: '', confirm: '' },
  });

  const submit = handleSubmit(async ({ current, password }) => {
    setFailure(null);
    try {
      await settings.changePassword({ currentPassword: current, newPassword: password });
      toast({ tone: 'success', title: 'Password changed', body: 'Your other devices were signed out.' });
      router.back();
    } catch (error) {
      setFailure(applyServerErrors(error, setError, { currentPassword: 'current', newPassword: 'password' }));
    }
  });

  return (
    <Screen header={<StackHeader title="Change password" />}>
      <View style={styles.form}>
        <Text.Body tone="secondary">Other devices signed in to this account will need to sign in again.</Text.Body>
        {failure ? <StatusBanner tone="error" title="Couldn't change your password" body={failure} testID="change-password-error" /> : null}
        <FormField
          control={control}
          name="current"
          label="Current password"
          secureTextEntry
          autoComplete="current-password"
          textContentType="password"
        />
        <FormField
          control={control}
          name="password"
          label="New password"
          secureTextEntry
          autoComplete="new-password"
          textContentType="newPassword"
        />
        <FormField
          control={control}
          name="confirm"
          label="Confirm new password"
          secureTextEntry
          autoComplete="new-password"
          textContentType="newPassword"
          returnKeyType="go"
          onSubmitEditing={() => void submit()}
        />
        <Button label="Change password" loading={formState.isSubmitting} onPress={() => void submit()} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  form: { gap: space[4] },
});
