import { zodResolver } from '@hookform/resolvers/zod';
import { router } from 'expo-router';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { AuthScaffold } from '@/components/AuthScaffold';
import { Button } from '@/components/Button';
import { FormField } from '@/components/FormField';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { auth } from '@/lib/api';
import { applyServerErrors, emailField, newPasswordField } from '@/lib/forms';

const schema = z.object({
  displayName: z.string().trim().max(80, 'Use at most 80 characters.'),
  email: emailField,
  password: newPasswordField,
});
type Values = z.infer<typeof schema>;

// PROPOSED screen (SYSTEM_DESIGN.md §8.2). The API answers 201 for new and existing emails alike (§11.3 A1).
export default function SignUpScreen() {
  const [failure, setFailure] = useState<string | null>(null);
  const { control, handleSubmit, setError, setFocus, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { displayName: '', email: '', password: '' },
  });

  const submit = handleSubmit(async ({ displayName, email, password }) => {
    setFailure(null);
    try {
      await auth.register({ email, password, displayName: displayName || undefined });
      router.replace({ pathname: '/verify-email', params: { email } });
    } catch (error) {
      setFailure(applyServerErrors(error, setError, { email: 'email', password: 'password', displayName: 'displayName' }));
    }
  });

  return (
    <AuthScaffold
      title="Create account"
      subtitle="Research US stocks and practise with a $100,000 paper account."
      footer={<Button variant="link" label="I already have an account" onPress={() => router.replace('/sign-in')} />}
    >
      {failure ? <StatusBanner tone="error" title="Couldn't create your account" body={failure} testID="sign-up-error" /> : null}
      <FormField control={control} name="displayName" label="Name (optional)" autoComplete="name" textContentType="name" returnKeyType="next" onSubmitEditing={() => setFocus('email')} />
      <FormField
        control={control}
        name="email"
        label="Email"
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
        secureTextEntry
        autoComplete="new-password"
        textContentType="newPassword"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />
      <Text.Caption tone="tertiary">At least 12 characters. A long passphrase works well.</Text.Caption>
      <Button label="Create account" loading={formState.isSubmitting} onPress={() => void submit()} />
    </AuthScaffold>
  );
}
