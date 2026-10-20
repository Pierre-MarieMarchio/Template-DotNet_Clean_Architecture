export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  readonly code: string;
  readonly traceId?: string;
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

export const unreadableResponseCode = 'response.unreadable';

export class ApiError extends Error {
  override readonly name = 'ApiError';

  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? `The API answered ${String(status)} (${problem.code}).`);
  }

  get code(): string {
    return this.problem.code;
  }

  get fieldErrors(): Readonly<Record<string, readonly string[]>> {
    return this.problem.errors ?? {};
  }

  errorsFor(field: string): readonly string[] {
    return this.fieldErrors[field] ?? [];
  }
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;

  return typeof candidate.code === 'string'
    && optional(candidate.title, 'string')
    && optional(candidate.detail, 'string')
    && optional(candidate.status, 'number')
    && (candidate.errors === undefined || isFieldErrors(candidate.errors));
}

export function toApiError(response: Response, body: unknown): ApiError {
  if (isProblemDetails(body)) {
    return new ApiError(response.status, body);
  }

  return new ApiError(response.status, {
    code: unreadableResponseCode,
    status: response.status,
    title: response.statusText || `HTTP ${String(response.status)}`,
  });
}

function optional(value: unknown, type: 'string' | 'number'): boolean {
  return value === undefined || typeof value === type;
}

function isFieldErrors(value: unknown): value is Record<string, readonly string[]> {
  return typeof value === 'object'
    && value !== null
    && Object.values(value).every(
      (messages: unknown) => Array.isArray(messages) && messages.every((message: unknown) => typeof message === 'string'),
    );
}
