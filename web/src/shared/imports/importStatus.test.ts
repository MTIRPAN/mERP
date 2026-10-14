import { describe, expect, it } from 'vitest';
import { ImportStatus } from '@/shared/api/generated/model';
import { hasImported, isImportRunning, toErrorRows } from './importStatus';

describe('import status', () => {
  it('polls only while the server works on the file', () => {
    expect([ImportStatus.Queued, ImportStatus.Validating, ImportStatus.Committing].every(isImportRunning)).toBe(true);
    expect([ImportStatus.Ready, ImportStatus.Invalid, ImportStatus.Completed, undefined].some(isImportRunning)).toBe(
      false,
    );
  });

  it('refreshes the list once records were written', () => {
    expect(hasImported(ImportStatus.Completed)).toBe(true);
    expect(hasImported(ImportStatus.PartiallyCompleted)).toBe(true);
    expect(hasImported(ImportStatus.Invalid)).toBe(false);
  });

  it('shows column headers instead of keys, and no row for errors of the whole file', () => {
    const rows = toErrorRows({
      errors: [
        { row: 3, column: 'companyCode', message: 'Şirket bulunamadı.' },
        { row: 4, column: 'other', message: 'x' },
        { row: 0, column: null, message: 'Dosyada içe aktarılacak satır yok.' },
      ],
      columnNames: { companyCode: 'Şirket kodu' },
    });

    expect(rows.map(({ row, column }) => [row, column])).toEqual([
      [3, 'Şirket kodu'],
      [4, 'other'],
      [null, null],
    ]);
  });
});
