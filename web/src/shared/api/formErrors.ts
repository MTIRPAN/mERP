import type { FormInstance } from 'antd';
import { isApiError } from './problem';

/**
 * Shows validation errors of an API response on the matching form fields. Field paths come in camelCase dotted
 * form ("address.city"). Returns true when at least one field error was applied.
 */
export const applyApiErrorsToForm = (form: FormInstance, error: unknown): boolean => {
  if (!isApiError(error) || !error.problem?.fieldErrors?.length) {
    return false;
  }

  const byField = new Map<string, string[]>();
  for (const fieldError of error.problem.fieldErrors) {
    byField.set(fieldError.field, [...(byField.get(fieldError.field) ?? []), fieldError.message]);
  }

  form.setFields([...byField].map(([field, errors]) => ({ name: field.split('.'), errors })));
  return true;
};
