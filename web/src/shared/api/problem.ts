/** Error body returned by the API (ProblemDetails with mERP extensions, see backend LocalizedProblemDetails). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  fieldErrors?: FieldProblem[];
}

export interface FieldProblem {
  field: string;
  code: string;
  message: string;
}

/** Thrown by the API client for every non-success response; `status` 0 means the server was unreachable. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | undefined;

  constructor(status: number, problem: ProblemDetails | undefined, options?: ErrorOptions) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`, options);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

export const isApiError = (error: unknown): error is ApiError => error instanceof ApiError;
