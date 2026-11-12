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
import { applyServerErrors, emailField } from '@/lib/forms';
import { signIn } from '@/lib/session';

const schema = z.object({ email: emailField, password: z.string().min(1, 'Enter your password.').max(128, 'Use at most 128 characters.') });
type Values = z.infer<typeof schema>;

// PROPOSED screen (SYSTEM_DESIGN.md §8.2). On success the (auth) layout moves on to the stashed deep link or Home.
export default function SignInScreen() {
  const params = useLocalSearchParams<{ email?: string }>();
  const [failure, setFailure] = useState<{ message: string; unverified: boolean } | null>(null);
  const { control, handleSubmit, setError, setFocus, setValue, getValues, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { email: params.email ?? '', password: '' },
  });

  const submit = handleSubmit(async ({ email, password }) => {
    setFailure(null);
    try {
      await signIn(email, password);
    } catch (error) {
      if (isApiError(error) && error.code === 'invalid_credentials') setValue('password', '');
      const message = applyServerErrors(error, setError, { email: 'email', password: 'password' });
      if (message) setFailure({ message, unverified: isApiError(error) && error.code === 'email_not_verified' });
    }
  });

  return (
    <AuthScaffold
      title="Sign in"
      subtitle="Paper trading only. No real money moves."
      footer={
        <>
          <Button variant="link" label="Forgot password?" onPress={() => router.push({ pathname: '/forgot-password', params: { email: getValues('email') } })} />
          <Button variant="link" label="Create account" onPress={() => router.push('/sign-up')} />
        </>
      }
    >
      {failure ? (
        <StatusBanner
          tone="error"
          title={failure.unverified ? 'Email not verified' : "Couldn't sign in"}
          body={failure.message}
          action={
            failure.unverified
              ? { label: 'Resend verification email', onPress: () => router.push({ pathname: '/verify-email', params: { email: getValues('email') } }) }
              : undefined
          }
          testID="sign-in-error"
        />
      ) : null}
      <FormField
        control={control}
        name="email"
        label="Email"
        testID="sign-in-email"
        autoCapitalize="none"
        autoComplete="email"
        keyboardType="email-address"
        textContentType="username"
        returnKeyType="next"
        onSubmitEditing={() => setFocus('password')}
      />
      <FormField
        control={control}
        name="password"
        label="Password"
        testID="sign-in-password"
        secureTextEntry
        autoComplete="current-password"
        textContentType="password"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />
      <Button label="Sign in" testID="sign-in-submit" loading={formState.isSubmitting} onPress={() => void submit()} />
    </AuthScaffold>
  );
}
