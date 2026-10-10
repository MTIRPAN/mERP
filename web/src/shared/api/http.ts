import { ApiError, type ProblemDetails } from './problem';

/** Hooks the application plugs into the API client (token, company, culture, auth reactions). */
export interface HttpConfiguration {
  getAccessToken: () => Promise<string | undefined>;
  getCompanyId: () => string | undefined;
  getCulture: () => string;
  onUnauthorized: () => void;
  onForbidden: () => void;
}

let configuration: HttpConfiguration = {
  getAccessToken: () => Promise.resolve(undefined),
  getCompanyId: () => undefined,
  getCulture: () => 'tr-TR',
  onUnauthorized: () => undefined,
  onForbidden: () => undefined,
};

export const configureHttp = (overrides: Partial<HttpConfiguration>): void => {
  configuration = { ...configuration, ...overrides };
};

const readProblem = async (response: Response): Promise<ProblemDetails | undefined> => {
  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('json')) {
    return undefined;
  }

  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return undefined;
  }
};

/**
 * Mutator used by every generated orval function (CLAUDE.md: no hand-written fetch calls). Adds the bearer token,
 * `Accept-Language` and `X-Company-Id`; turns error responses into {@link ApiError}.
 */
export const customFetch = async <T>(url: string, options: RequestInit = {}): Promise<T> => {
  const headers = new Headers(options.headers);
  const token = await configuration.getAccessToken();
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  headers.set('Accept-Language', configuration.getCulture());
  const companyId = configuration.getCompanyId();
  if (companyId) {
    headers.set('X-Company-Id', companyId);
  }

  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  let response: Response;
  try {
    response = await fetch(url, { ...options, headers });
  } catch (error) {
    throw new ApiError(0, undefined, { cause: error });
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    if (response.status === 401) {
      configuration.onUnauthorized();
    } else if (response.status === 403) {
      configuration.onForbidden();
    }

    throw new ApiError(response.status, problem);
  }

  if (response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T;
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('json')) {
    return (await response.json()) as T;
  }

  // Files (Excel exports, attachments, even text ones) stay binary; see download.ts
  const isAttachment = (response.headers.get('content-disposition') ?? '').startsWith('attachment');
  return (contentType.startsWith('text/') && !isAttachment ? await response.text() : await response.blob()) as T;
};

export default customFetch;

/** Error type of the generated query hooks (picked up by orval from the mutator file). */
export type ErrorType<TError> = TError extends unknown ? ApiError : never;
