import { isApiError } from '@astryx/api-client';
import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { z } from 'zod';

// Client-side mirrors of the Identity validators (RegisterRequestValidator etc.); the server stays authoritative.
export const emailField = z
  .string()
  .trim()
  .min(1, 'Enter your email.')
  .max(254, 'Use at most 254 characters.')
  .pipe(z.email('Enter a valid email address.'));
export const newPasswordField = z.string().min(12, 'Use at least 12 characters.').max(128, 'Use at most 128 characters.');

/** A user-facing sentence for an API failure that is not tied to one field. */
export function describeError(error: unknown): string {
  if (!isApiError(error)) return 'Something went wrong. Try again.';
  if (error.isNetworkError) return "Can't reach Astryx. Check your connection and try again.";
  if (error.status === 429) return 'Too many attempts. Wait a minute and try again.';
  if (error.status >= 500) return 'Astryx is having trouble right now. Try again shortly.';
  return error.detail ?? error.title ?? 'Something went wrong. Try again.';
}

/**
 * Maps a 422's field errors onto the form (SYSTEM_DESIGN.md §8.10). `fields` maps server field names to form fields,
 * matched case-insensitively. Returns the message for the form as a whole, or null when every error found a field.
 */
export function applyServerErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: Record<string, Path<T>>,
): string | null {
  if (!isApiError(error) || error.status !== 422 || !error.errors) return describeError(error);

  const byLowerName = new Map(Object.entries(fields).map(([server, form]) => [server.toLowerCase(), form]));
  const unmatched: string[] = [];
  for (const [key, messages] of Object.entries(error.errors)) {
    const field = byLowerName.get(key.toLowerCase());
    if (field && messages[0]) setError(field, { type: 'server', message: messages[0] }, { shouldFocus: true });
    else unmatched.push(...messages);
  }
  if (unmatched.length > 0) return unmatched.join(' ');
  return Object.keys(error.errors).length > 0 ? null : describeError(error);
}
