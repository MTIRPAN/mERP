import { describe, expect, it } from 'vitest';
import { ApiError } from '@/shared/api/problem';
import { otherFieldErrors, templateError } from './templateErrors';

const problem = (...fieldErrors: { field: string; message: string }[]) =>
  new ApiError(400, { status: 400, fieldErrors: fieldErrors.map((e) => ({ ...e, code: 'x' })) });

describe('templateErrors', () => {
  it('reads the line of a body error', () => {
    expect(templateError(problem({ field: 'body', message: 'Şablon 12. satırda hatalı: kapanmamış etiket' }))).toEqual({
      line: 12,
      message: 'Şablon 12. satırda hatalı: kapanmamış etiket',
    });
  });

  it('falls back to the first line, and ignores other errors', () => {
    expect(templateError(problem({ field: 'body', message: 'Şablon çok uzun' }))?.line).toBe(1);
    expect(templateError(problem({ field: 'culture', message: 'Dil kodu geçersiz.' }))).toBeUndefined();
    expect(templateError(new Error('network'))).toBeUndefined();
  });

  it('lists the other field errors', () => {
    expect(
      otherFieldErrors(
        problem({ field: 'body', message: 'Şablon 1. satırda hatalı' }, { field: 'documentType', message: 'Var.' }),
      ),
    ).toEqual(['Var.']);
  });
});
