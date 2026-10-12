import type { FormInstance } from 'antd';
import { isApiError } from './problem';

/** "steps[0].approver.resolver" → ['steps', 0, 'approver', 'resolver'], the path of the antd form field. */
export const fieldPath = (field: string): (string | number)[] =>
  field
    .split('.')
    .flatMap((part) => part.split(/\[(\d+)\]/).filter((piece) => piece !== ''))
    .map((piece) => (/^\d+$/.test(piece) ? Number(piece) : piece));

/**
 * Shows validation errors of an API response on the matching form fields. Field paths come in camelCase dotted
 * form ("address.city", "steps[0].condition"); `aliases` moves an API field onto another form field (e.g. "name" →
 * "name.tr-TR"). Returns true when at least one field error was applied.
 */
export const applyApiErrorsToForm = (
  form: FormInstance,
  error: unknown,
  aliases: Record<string, (string | number)[]> = {},
): boolean => {
  if (!isApiError(error) || !error.problem?.fieldErrors?.length) {
    return false;
  }

  const byField = new Map<string, string[]>();
  for (const fieldError of error.problem.fieldErrors) {
    byField.set(fieldError.field, [...(byField.get(fieldError.field) ?? []), fieldError.message]);
  }

  form.setFields([...byField].map(([field, errors]) => ({ name: aliases[field] ?? fieldPath(field), errors })));
  return true;
};
