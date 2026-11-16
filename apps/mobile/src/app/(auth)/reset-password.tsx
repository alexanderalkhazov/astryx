import { zodResolver } from '@hookform/resolvers/zod';
import { isApiError } from '@astryx/api-client';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { AuthScaffold } from '@/components/AuthScaffold';
import { Button } from '@/components/Button';
import { FormField } from '@/components/FormField';
import { StatusBanner } from '@/components/StatusBanner';
import { auth, clearLocalSession } from '@/lib/api';
import { applyServerErrors, newPasswordField } from '@/lib/forms';
import { useSession } from '@/state/session';
import { toast } from '@/state/toasts';

const schema = z
  .object({ password: newPasswordField, confirm: z.string() })
  .refine((v) => v.password === v.confirm, { path: ['confirm'], message: "Passwords don't match." });
type Values = z.infer<typeof schema>;

// PROPOSED screen (SYSTEM_DESIGN.md §8.2), opened from astryx://reset-password?userId=…&token=… (§11.3 A9).
export default function ResetPasswordScreen() {
  const { userId, token } = useLocalSearchParams<{ userId?: string; token?: string }>();
  const [failure, setFailure] = useState<{ message: string; badLink: boolean } | null>(
    userId && token ? null : { message: 'This reset link is incomplete. Request a new one.', badLink: true },
  );
  const { control, handleSubmit, setError, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { password: '', confirm: '' },
  });

  const submit = handleSubmit(async ({ password }) => {
    if (!userId || !token) return;
    setFailure(null);
    try {
      await auth.resetPassword({ userId, token, newPassword: password });
      // The reset ends every session, this device's included.
      if (useSession.getState().status === 'signedIn') await clearLocalSession(true);
      toast({ tone: 'success', title: 'Password updated', body: 'Sign in with your new password.' });
      router.replace('/sign-in');
    } catch (error) {
      const message = applyServerErrors(error, setError, { newPassword: 'password' });
      if (message) setFailure({ message, badLink: isApiError(error) && error.code === 'invalid_or_expired_token' });
    }
  });

  return (
    <AuthScaffold
      title="Choose a new password"
      subtitle="You'll be signed out on every device."
      footer={<Button variant="link" label="Back to sign in" onPress={() => router.replace('/sign-in')} />}
    >
      {failure ? (
        <StatusBanner
          tone="error"
          title="Couldn't reset your password"
          body={failure.message}
          action={failure.badLink ? { label: 'Request a new link', onPress: () => router.replace('/forgot-password') } : undefined}
          testID="reset-error"
        />
      ) : null}
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
      <Button label="Update password" loading={formState.isSubmitting} disabled={!userId || !token} onPress={() => void submit()} />
    </AuthScaffold>
  );
}
