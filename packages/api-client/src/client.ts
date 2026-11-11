import type { z } from 'zod';

import { ApiError, toApiError } from './errors';

export interface ApiClientOptions {
  baseUrl: string;
  /** Current access token (memory only), or null when signed out. */
  getAccessToken: () => string | null;
  /**
   * Exchanges the stored refresh token for new tokens and returns the new access token.
   * Throws when the session can't be renewed. Called at most once for any number of concurrent 401s.
   */
  refresh: () => Promise<string>;
  /** Called once when a refresh fails: clear the session and send the user to sign in. */
  onSessionExpired: () => void;
  newCorrelationId: () => string;
  fetchImpl?: typeof fetch;
}

export interface RequestOptions<T> {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  /** Validates and types the response body. Omit for empty responses. */
  schema?: z.ZodType<T>;
  /** Sent on money-moving requests; reuse the same key when retrying the same intent (SYSTEM_DESIGN.md §11.1). */
  idempotencyKey?: string;
  ifMatch?: string;
  /** False for endpoints that work without a session (login, register, refresh, …). */
  auth?: boolean;
  signal?: AbortSignal;
}

export interface ApiResponse<T> {
  data: T;
  status: number;
  headers: Headers;
}

/**
 * HTTP client for the Astryx API (SYSTEM_DESIGN.md §8.8): auth header, correlation ID, idempotency key,
 * RFC 7807 errors, response validation, and a single-flight token refresh on 401.
 */
export function createApiClient(options: ApiClientOptions) {
  // Resolved per call, so a replaced global fetch (tests, interceptors) is honoured.
  const fetchImpl: typeof fetch = (input, init) => (options.fetchImpl ?? globalThis.fetch)(input, init);
  let inflightRefresh: Promise<string> | null = null;

  /** One refresh at a time: concurrent callers share the same promise (and the same outcome). */
  function refreshOnce(): Promise<string> {
    if (!inflightRefresh) {
      inflightRefresh = options.refresh().finally(() => {
        inflightRefresh = null;
      });
    }
    return inflightRefresh;
  }

  async function send(path: string, init: RequestOptions<unknown>, accessToken: string | null, correlationId: string): Promise<Response> {
    const headers: Record<string, string> = { Accept: 'application/json', 'X-Correlation-Id': correlationId };
    if (init.body !== undefined) headers['Content-Type'] = 'application/json';
    if (accessToken && init.auth !== false) headers.Authorization = `Bearer ${accessToken}`;
    if (init.idempotencyKey) headers['Idempotency-Key'] = init.idempotencyKey;
    if (init.ifMatch) headers['If-Match'] = init.ifMatch;

    try {
      return await fetchImpl(`${options.baseUrl}${path}`, {
        method: init.method ?? 'GET',
        headers,
        body: init.body === undefined ? undefined : JSON.stringify(init.body),
        signal: init.signal,
      });
    } catch (error) {
      if (init.signal?.aborted) throw error;
      throw new ApiError({ status: 0, code: 'network_error', detail: 'The API could not be reached. Check your connection.', correlationId });
    }
  }

  async function request<T = void>(path: string, init: RequestOptions<T> = {}): Promise<ApiResponse<T>> {
    const correlationId = options.newCorrelationId();
    let response = await send(path, init, options.getAccessToken(), correlationId);

    if (response.status === 401 && init.auth !== false) {
      let renewed: string;
      try {
        renewed = await refreshOnce();
      } catch {
        // Only the first failure in a burst reaches here with an active session; the handler must be idempotent.
        options.onSessionExpired();
        throw new ApiError({ status: 401, code: 'session_expired', detail: 'Your session has ended. Sign in again.', correlationId });
      }

      response = await send(path, init, renewed, correlationId);
    }

    if (!response.ok) {
      throw await toApiError(response, correlationId);
    }

    const data = await parse(response, init.schema, correlationId);
    return { data, status: response.status, headers: response.headers };
  }

  return { request };
}

async function parse<T>(response: Response, schema: z.ZodType<T> | undefined, correlationId: string): Promise<T> {
  if (!schema) return undefined as T;
  const text = await response.text();
  let json: unknown;
  try {
    json = text ? JSON.parse(text) : undefined;
  } catch {
    throw new ApiError({ status: response.status, code: 'invalid_response', detail: 'The API returned malformed JSON.', correlationId });
  }

  const result = schema.safeParse(json);
  if (!result.success) {
    throw new ApiError({ status: response.status, code: 'invalid_response', detail: `Unexpected response shape: ${result.error.message}`, correlationId });
  }

  return result.data;
}

export type ApiClient = ReturnType<typeof createApiClient>;
