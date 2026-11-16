import { ApiError } from '@astryx/api-client';
import { Text } from 'react-native';
import { act, fireEvent, renderRouter, screen, waitFor } from 'expo-router/testing-library';

import AppLayout from '../(app)/_layout';
import AuthLayout from '../(auth)/_layout';
import ForgotPasswordScreen from '../(auth)/forgot-password';
import ResetPasswordScreen from '../(auth)/reset-password';
import SignInScreen from '../(auth)/sign-in';
import SignUpScreen from '../(auth)/sign-up';
import VerifyEmailScreen from '../(auth)/verify-email';
import { usePendingLink } from '@/state/pendingLink';
import { useSession } from '@/state/session';

jest.mock('expo-crypto', () => ({ randomUUID: () => '00000000-0000-4000-8000-000000000001' }));
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'WHEN_UNLOCKED_THIS_DEVICE_ONLY',
  getItemAsync: jest.fn(async () => null),
  setItemAsync: jest.fn(async () => undefined),
  deleteItemAsync: jest.fn(async () => undefined),
}));

const mockSignIn = jest.fn();
jest.mock('@/lib/session', () => ({ signIn: (...args: unknown[]) => mockSignIn(...args) }));

jest.mock('@/lib/api', () => ({
  ...jest.requireActual('@/lib/api'),
  auth: { register: jest.fn(), verifyEmail: jest.fn(), resendVerification: jest.fn(), forgotPassword: jest.fn(), resetPassword: jest.fn() },
}));
const mockAuth = (
  jest.requireMock('@/lib/api') as {
    auth: Record<'register' | 'verifyEmail' | 'resendVerification' | 'forgotPassword' | 'resetPassword', jest.Mock>;
  }
).auth;

const routes = {
  '(app)/_layout': AppLayout,
  '(app)/index': () => <Text>Home</Text>,
  '(app)/stock/[ticker]': () => <Text>Stock</Text>,
  '(app)/settings': () => <Text>Settings</Text>,
  '(auth)/_layout': AuthLayout,
  '(auth)/sign-in': SignInScreen,
  '(auth)/sign-up': SignUpScreen,
  '(auth)/verify-email': VerifyEmailScreen,
  '(auth)/forgot-password': ForgotPasswordScreen,
  '(auth)/reset-password': ResetPasswordScreen,
};

const signedIn = () => useSession.getState().signIn('access-1', '2026-11-11T12:15:00Z');
const ME_TOKEN = 'a'.repeat(43);

beforeEach(() => {
  jest.clearAllMocks();
  useSession.setState({ status: 'signedOut', accessToken: null, accessTokenExpiresAt: null, user: null, signedOutByUser: false });
  usePendingLink.setState({ href: null });
  mockSignIn.mockImplementation(async () => signedIn());
});

describe('auth gate', () => {
  it('sends a signed-out deep link to sign-in and replays it after signing in', async () => {
    await renderRouter(routes, { initialUrl: '/stock/NVDA?tab=news' });

    await waitFor(() => expect(screen).toHavePathname('/sign-in'));
    expect(usePendingLink.getState().href).toBe('/stock/NVDA?tab=news');

    await fireEvent.changeText(screen.getByLabelText('Email'), 'trader@example.com');
    await fireEvent.changeText(screen.getByLabelText('Password'), 'correct horse battery staple');
    await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(screen).toHavePathname('/stock/NVDA'));
    expect(screen).toHaveSearchParams({ ticker: 'NVDA', tab: 'news' });
    expect(mockSignIn).toHaveBeenCalledWith('trader@example.com', 'correct horse battery staple');
    expect(usePendingLink.getState().href).toBeNull(); // replayed once
  });

  it('does not remember the screen after an explicit sign-out', async () => {
    signedIn();
    await renderRouter(routes, { initialUrl: '/settings' });
    expect(screen.getByText('Settings')).toBeOnTheScreen();

    await act(async () => useSession.getState().signOut(true));

    await waitFor(() => expect(screen).toHavePathname('/sign-in'));
    expect(usePendingLink.getState().href).toBeNull();
  });

  it('keeps a signed-in user out of the sign-in screens', async () => {
    signedIn();
    await renderRouter(routes, { initialUrl: '/sign-up' });

    await waitFor(() => expect(screen).toHavePathname('/'));
  });

  it('renders nothing until the stored session has been checked', async () => {
    useSession.setState({ status: 'unknown' });
    await renderRouter(routes, { initialUrl: '/stock/NVDA' });

    expect(screen.queryByText('Stock')).toBeNull();
    expect(screen.queryByText('Sign in')).toBeNull();
  });
});

describe('sign in', () => {
  it('validates on the device before calling the API', async () => {
    await renderRouter(routes, { initialUrl: '/sign-in' });

    await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Enter your email.')).toBeOnTheScreen();
    expect(screen.getByText('Enter your password.')).toBeOnTheScreen();
    expect(mockSignIn).not.toHaveBeenCalled();
  });

  it('shows the server reason for wrong credentials and clears the password', async () => {
    mockSignIn.mockRejectedValue(new ApiError({ status: 401, code: 'invalid_credentials', detail: 'Email or password is incorrect.' }));
    await renderRouter(routes, { initialUrl: '/sign-in' });

    await fireEvent.changeText(screen.getByLabelText('Email'), 'trader@example.com');
    await fireEvent.changeText(screen.getByLabelText('Password'), 'wrong password!');
    await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Email or password is incorrect.')).toBeOnTheScreen();
    expect(screen.getByLabelText('Password').props.value).toBe('');
    expect(screen).toHavePathname('/sign-in');
  });

  it('offers to resend the link when the email is not verified', async () => {
    mockSignIn.mockRejectedValue(new ApiError({ status: 403, code: 'email_not_verified', detail: 'Verify your email before signing in.' }));
    await renderRouter(routes, { initialUrl: '/sign-in' });

    await fireEvent.changeText(screen.getByLabelText('Email'), 'trader@example.com');
    await fireEvent.changeText(screen.getByLabelText('Password'), 'correct horse battery staple');
    await fireEvent.press(screen.getByRole('button', { name: 'Sign in' }));
    await fireEvent.press(await screen.findByRole('button', { name: 'Resend verification email' }));

    await waitFor(() => expect(screen).toHavePathname('/verify-email'));
    expect(screen).toHaveSearchParams({ email: 'trader@example.com' });
    expect(screen.getByText(/We sent a verification link to trader@example.com/)).toBeOnTheScreen();
  });
});

describe('sign up', () => {
  it('maps 422 field errors from the server onto the form', async () => {
    mockAuth.register.mockRejectedValue(
      new ApiError({
        status: 422,
        code: 'validation_failed',
        errors: { password: ['This password appeared in a data breach. Choose another.'] },
      }),
    );
    await renderRouter(routes, { initialUrl: '/sign-up' });

    await fireEvent.changeText(screen.getByLabelText('Email'), 'new@example.com');
    await fireEvent.changeText(screen.getByLabelText('Password'), 'password123456');
    await fireEvent.press(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByText('This password appeared in a data breach. Choose another.')).toBeOnTheScreen();
    expect(screen.queryByTestId('sign-up-error')).toBeNull();
  });

  it('goes to "check your inbox" after registering', async () => {
    mockAuth.register.mockResolvedValue({ data: { emailVerificationRequired: true } });
    await renderRouter(routes, { initialUrl: '/sign-up' });

    await fireEvent.changeText(screen.getByLabelText('Email'), 'new@example.com');
    await fireEvent.changeText(screen.getByLabelText('Password'), 'correct horse battery staple');
    await fireEvent.press(screen.getByRole('button', { name: 'Create account' }));

    await waitFor(() => expect(screen).toHavePathname('/verify-email'));
    expect(mockAuth.register).toHaveBeenCalledWith({
      email: 'new@example.com',
      password: 'correct horse battery staple',
      displayName: undefined,
    });
  });
});

describe('email links', () => {
  it('verifies the email from the link exactly once', async () => {
    mockAuth.verifyEmail.mockResolvedValue({ data: undefined });
    await renderRouter(routes, { initialUrl: `/verify-email?userId=01a1c9d4-85c3-7a85-beb8-a9ad35024384&token=${ME_TOKEN}` });

    expect(await screen.findByText('Email verified')).toBeOnTheScreen();
    expect(mockAuth.verifyEmail).toHaveBeenCalledTimes(1);
    expect(mockAuth.verifyEmail).toHaveBeenCalledWith({ userId: '01a1c9d4-85c3-7a85-beb8-a9ad35024384', token: ME_TOKEN });

    await fireEvent.press(screen.getByRole('button', { name: 'Continue to sign in' }));
    await waitFor(() => expect(screen).toHavePathname('/sign-in'));
  });

  it('opens verification links even while signed in', async () => {
    signedIn();
    mockAuth.verifyEmail.mockResolvedValue({ data: undefined });
    await renderRouter(routes, { initialUrl: `/verify-email?userId=u1&token=${ME_TOKEN}` });

    expect(await screen.findByText('Email verified')).toBeOnTheScreen();
    expect(screen).toHavePathname('/verify-email');
  });

  it('offers a new reset link when the token is no longer valid', async () => {
    mockAuth.resetPassword.mockRejectedValue(
      new ApiError({
        status: 422,
        code: 'invalid_or_expired_token',
        detail: 'This reset link is invalid or has expired. Request a new one.',
      }),
    );
    await renderRouter(routes, { initialUrl: `/reset-password?userId=u1&token=${ME_TOKEN}` });

    await fireEvent.changeText(screen.getByLabelText('New password'), 'a brand new passphrase');
    await fireEvent.changeText(screen.getByLabelText('Confirm new password'), 'a brand new passphrase');
    await fireEvent.press(screen.getByRole('button', { name: 'Update password' }));
    await fireEvent.press(await screen.findByRole('button', { name: 'Request a new link' }));

    await waitFor(() => expect(screen).toHavePathname('/forgot-password'));
  });

  it('checks the confirmation matches before resetting', async () => {
    await renderRouter(routes, { initialUrl: `/reset-password?userId=u1&token=${ME_TOKEN}` });

    await fireEvent.changeText(screen.getByLabelText('New password'), 'a brand new passphrase');
    await fireEvent.changeText(screen.getByLabelText('Confirm new password'), 'a different passphrase');
    await fireEvent.press(screen.getByRole('button', { name: 'Update password' }));

    expect(await screen.findByText("Passwords don't match.")).toBeOnTheScreen();
    expect(mockAuth.resetPassword).not.toHaveBeenCalled();
  });

  it('resets the password and returns to sign-in', async () => {
    mockAuth.resetPassword.mockResolvedValue({ data: undefined });
    await renderRouter(routes, { initialUrl: `/reset-password?userId=u1&token=${ME_TOKEN}` });

    await fireEvent.changeText(screen.getByLabelText('New password'), 'a brand new passphrase');
    await fireEvent.changeText(screen.getByLabelText('Confirm new password'), 'a brand new passphrase');
    await fireEvent.press(screen.getByRole('button', { name: 'Update password' }));

    await waitFor(() => expect(screen).toHavePathname('/sign-in'));
    expect(mockAuth.resetPassword).toHaveBeenCalledWith({ userId: 'u1', token: ME_TOKEN, newPassword: 'a brand new passphrase' });
  });
});
