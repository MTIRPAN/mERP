import { isApiError } from '@/shared/api/problem';

export interface TemplateError {
  /** 1-based line of the template body; 1 when the message names none. */
  line: number;
  message: string;
}

/**
 * The template error of an API response (D36): a field error on <c>body</c>. Its localized message starts with the
 * line ("Şablon 3. satırda hatalı: …"), and field errors carry no arguments, so the first number is the line.
 */
export const templateError = (error: unknown, field = 'body'): TemplateError | undefined => {
  if (!isApiError(error)) {
    return undefined;
  }

  const problem = error.problem?.fieldErrors?.find((e) => e.field === field);
  if (!problem) {
    return undefined;
  }

  const line = /\d+/.exec(problem.message)?.[0];
  return { line: line ? Number(line) : 1, message: problem.message };
};

/** Field errors other than the body's, e.g. a duplicate type, company and culture. */
export const otherFieldErrors = (error: unknown, field = 'body'): string[] =>
  isApiError(error) ? (error.problem?.fieldErrors ?? []).filter((e) => e.field !== field).map((e) => e.message) : [];
