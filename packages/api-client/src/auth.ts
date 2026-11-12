import { z } from 'zod';

import type { ApiClient } from './client';
import type { components } from './generated/schema';

// Checked against the types generated from the API's OpenAPI document (pnpm api:gen).

export const TokenResponse = z.object({
  accessToken: z.string().min(1),
  accessTokenExpiresAt: z.string(),
  refreshToken: z.string().min(1),
  refreshTokenExpiresAt: z.string(),
  tokenType: z.literal('Bearer'),
}) satisfies z.ZodType<components['schemas']['TokenResponse']>;
export type TokenResponse = z.infer<typeof TokenResponse>;

export const CurrentUser = z.object({
  id: z.string().uuid(),
  email: z.string(),
  emailVerified: z.boolean(),
  displayName: z.string().nullable(),
  timezone: z.string(),
  locale: z.string(),
  biometricLock: z.boolean(),
  tradingMode: z.literal('paper'),
  features: z.object({ liveTrading: z.boolean(), strategyConfigure: z.boolean(), push: z.boolean() }),
}) satisfies z.ZodType<components['schemas']['CurrentUserResponse']>;
export type CurrentUser = z.infer<typeof CurrentUser>;

export const RegisterResponse = z.object({ emailVerificationRequired: z.literal(true) }) satisfies z.ZodType<components['schemas']['RegisterResponse']>;

/** Auth endpoints (SYSTEM_DESIGN.md §11.3). All except logout and me work without a session. */
export function authApi(client: ApiClient) {
  return {
    register: (body: { email: string; password: string; displayName?: string }) =>
      client.request('/api/v1/auth/register', { method: 'POST', body, schema: RegisterResponse, auth: false }),
    verifyEmail: (body: { userId: string; token: string }) => client.request('/api/v1/auth/verify-email', { method: 'POST', body, auth: false }),
    resendVerification: (body: { email: string }) => client.request('/api/v1/auth/resend-verification', { method: 'POST', body, auth: false }),
    login: (body: { email: string; password: string; deviceId: string }) =>
      client.request('/api/v1/auth/login', { method: 'POST', body, schema: TokenResponse, auth: false }),
    refresh: (body: { refreshToken: string; deviceId: string }) =>
      client.request('/api/v1/auth/refresh', { method: 'POST', body, schema: TokenResponse, auth: false }),
    logout: (body: { refreshToken: string }) => client.request('/api/v1/auth/logout', { method: 'POST', body }),
    forgotPassword: (body: { email: string }) => client.request('/api/v1/auth/forgot-password', { method: 'POST', body, auth: false }),
    resetPassword: (body: { userId: string; token: string; newPassword: string }) =>
      client.request('/api/v1/auth/reset-password', { method: 'POST', body, auth: false }),
    me: () => client.request('/api/v1/me', { schema: CurrentUser }),
  };
}
