import { zodResolver } from '@hookform/resolvers/zod';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { AuthScaffold } from '@/components/AuthScaffold';
import { Button } from '@/components/Button';
import { FormField } from '@/components/FormField';
import { StatusBanner } from '@/components/StatusBanner';
import { auth } from '@/lib/api';
import { applyServerErrors, emailField } from '@/lib/forms';

const schema = z.object({ email: emailField });
type Values = z.infer<typeof schema>;

// PROPOSED screen (SYSTEM_DESIGN.md §8.2). The API answers the same for unknown emails (§11.3 A8).
export default function ForgotPasswordScreen() {
  const params = useLocalSearchParams<{ email?: string }>();
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const { control, handleSubmit, setError, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { email: params.email ?? '' },
  });

  const submit = handleSubmit(async ({ email }) => {
    setFailure(null);
    try {
      await auth.forgotPassword({ email });
      setSentTo(email);
    } catch (error) {
      setFailure(applyServerErrors(error, setError, { email: 'email' }));
    }
  });

  const back = (
    <Button
      variant="link"
      label="Back to sign in"
      onPress={() => router.replace({ pathname: '/sign-in', params: sentTo ? { email: sentTo } : {} })}
    />
  );

  if (sentTo) {
    return (
      <AuthScaffold
        title="Check your inbox"
        subtitle={`If an account exists for ${sentTo}, we've sent a link to reset its password. It works once and expires in 30 minutes.`}
        footer={back}
      >
        <Button variant="secondary" label="Use a different email" onPress={() => setSentTo(null)} />
      </AuthScaffold>
    );
  }

  return (
    <AuthScaffold title="Reset your password" subtitle="Enter your account's email and we'll send you a reset link." footer={back}>
      {failure ? <StatusBanner tone="error" title="Couldn't send the link" body={failure} testID="forgot-error" /> : null}
      <FormField
        control={control}
        name="email"
        label="Email"
        autoCapitalize="none"
        autoComplete="email"
        keyboardType="email-address"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />
      <Button label="Send reset link" loading={formState.isSubmitting} onPress={() => void submit()} />
    </AuthScaffold>
  );
}
