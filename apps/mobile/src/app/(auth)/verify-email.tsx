import { zodResolver } from '@hookform/resolvers/zod';
import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';

import { AuthScaffold } from '@/components/AuthScaffold';
import { Button } from '@/components/Button';
import { Spinner } from '@/components/Feedback';
import { FormField } from '@/components/FormField';
import { StatusBanner } from '@/components/StatusBanner';
import { auth } from '@/lib/api';
import { applyServerErrors, describeError, emailField } from '@/lib/forms';
import { useSession } from '@/state/session';
import { toast } from '@/state/toasts';

type Verification = { state: 'verifying' } | { state: 'verified' } | { state: 'failed'; message: string };

// PROPOSED screen (SYSTEM_DESIGN.md §8.2). Two entry points: the emailed link astryx://verify-email?userId=…&token=…,
// and "check your inbox" after sign-up or an unverified sign-in (?email=…).
export default function VerifyEmailScreen() {
  const { userId, token, email } = useLocalSearchParams<{ userId?: string; token?: string; email?: string }>();
  const fromLink = Boolean(userId && token);
  const [verification, setVerification] = useState<Verification>({ state: 'verifying' });
  const started = useRef(false);

  useEffect(() => {
    if (!fromLink || started.current) return;
    started.current = true; // links are single-use: never submit twice
    auth
      .verifyEmail({ userId: userId!, token: token! })
      .then(() => setVerification({ state: 'verified' }))
      .catch((error: unknown) => setVerification({ state: 'failed', message: describeError(error) }));
  }, [fromLink, userId, token]);

  if (fromLink && verification.state === 'verifying') {
    return (
      <AuthScaffold title="Verifying your email">
        <Spinner label="Verifying email" />
      </AuthScaffold>
    );
  }

  if (fromLink && verification.state === 'verified') {
    return <Verified />;
  }

  return (
    <AuthScaffold
      title={fromLink ? 'Link expired' : 'Check your inbox'}
      subtitle={
        fromLink
          ? undefined
          : email
            ? `We sent a verification link to ${email}. Open it on this phone to finish setting up your account.`
            : 'Open the verification link we emailed you on this phone to finish setting up your account.'
      }
      footer={
        <Button
          variant="link"
          label="Back to sign in"
          onPress={() => router.replace({ pathname: '/sign-in', params: email ? { email } : {} })}
        />
      }
    >
      {verification.state === 'failed' ? (
        <StatusBanner tone="error" title="Couldn't verify your email" body={verification.message} testID="verify-error" />
      ) : null}
      <ResendForm email={email ?? ''} />
    </AuthScaffold>
  );
}

function Verified() {
  const signedIn = useSession((s) => s.status === 'signedIn');
  return (
    <AuthScaffold title="Email verified" subtitle="Your account is ready.">
      <Button label={signedIn ? 'Continue' : 'Continue to sign in'} onPress={() => router.replace(signedIn ? '/' : '/sign-in')} />
    </AuthScaffold>
  );
}

const resendSchema = z.object({ email: emailField });

function ResendForm({ email }: { email: string }) {
  const [failure, setFailure] = useState<string | null>(null);
  const { control, handleSubmit, setError, formState } = useForm<z.infer<typeof resendSchema>>({
    resolver: zodResolver(resendSchema),
    defaultValues: { email },
  });

  const submit = handleSubmit(async (values) => {
    setFailure(null);
    try {
      await auth.resendVerification(values);
      // 202 either way: the API does not reveal whether the email has an unverified account.
      toast({ tone: 'success', title: 'Verification email sent', body: `If ${values.email} needs verifying, a new link is on its way.` });
    } catch (error) {
      setFailure(applyServerErrors(error, setError, { email: 'email' }));
    }
  });

  return (
    <>
      {failure ? <StatusBanner tone="error" title="Couldn't send the email" body={failure} /> : null}
      <FormField control={control} name="email" label="Email" autoCapitalize="none" autoComplete="email" keyboardType="email-address" />
      <Button variant="secondary" label="Resend verification email" loading={formState.isSubmitting} onPress={() => void submit()} />
    </>
  );
}
