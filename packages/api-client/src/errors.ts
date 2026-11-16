/** An RFC 7807 problem from the API, or a client-side failure, with a stable machine-readable code (SYSTEM_DESIGN.md §11.2). */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly title: string | undefined;
  readonly detail: string | undefined;
  /** Field errors from a 422 validation problem, keyed by camelCase field name. */
  readonly errors: Record<string, string[]> | undefined;
  readonly traceId: string | undefined;
  readonly correlationId: string | undefined;
  /** Problem extension members beyond the standard ones, e.g. `jobId` on a 409 `duplicate` analysis request. */
  readonly extensions: Record<string, unknown>;

  constructor(init: {
    status: number;
    code: string;
    title?: string;
    detail?: string;
    errors?: Record<string, string[]>;
    traceId?: string;
    correlationId?: string;
    extensions?: Record<string, unknown>;
  }) {
    super(init.detail ?? init.title ?? init.code);
    this.name = 'ApiError';
    this.status = init.status;
    this.code = init.code;
    this.title = init.title;
    this.detail = init.detail;
    this.errors = init.errors;
    this.traceId = init.traceId;
    this.correlationId = init.correlationId;
    this.extensions = init.extensions ?? {};
  }

  /** The request never reached the API (offline, DNS, timeout). */
  get isNetworkError(): boolean {
    return this.code === 'network_error';
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

const STANDARD_MEMBERS = new Set(['type', 'title', 'status', 'detail', 'instance', 'code', 'errors', 'traceId', 'correlationId']);

interface ProblemBody {
  [member: string]: unknown;
  code?: unknown;
  title?: unknown;
  detail?: unknown;
  errors?: unknown;
  traceId?: unknown;
  correlationId?: unknown;
}

const str = (value: unknown) => (typeof value === 'string' ? value : undefined);

/** Turns a non-2xx response into an ApiError, reading problem+json when present. */
export async function toApiError(response: Response, correlationId: string): Promise<ApiError> {
  let body: ProblemBody = {};
  try {
    const text = await response.text();
    body = text ? (JSON.parse(text) as ProblemBody) : {};
  } catch {
    body = {};
  }

  const errors = body.errors && typeof body.errors === 'object' ? (body.errors as Record<string, string[]>) : undefined;
  return new ApiError({
    status: response.status,
    code: str(body.code) ?? `http_${response.status}`,
    title: str(body.title),
    detail: str(body.detail),
    errors,
    traceId: str(body.traceId),
    correlationId: str(body.correlationId) ?? correlationId,
    extensions: Object.fromEntries(Object.entries(body).filter(([member]) => !STANDARD_MEMBERS.has(member))),
  });
}
