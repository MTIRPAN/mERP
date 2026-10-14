import { ImportStatus, type ImportJobDetails, type ImportJobError } from '@/shared/api/generated/model';

/** While the server works on the file, the drawer polls the job. */
export const isImportRunning = (status?: ImportStatus): boolean =>
  status === ImportStatus.Queued || status === ImportStatus.Validating || status === ImportStatus.Committing;

/** The import wrote records: the page's list must refresh. */
export const hasImported = (status?: ImportStatus): boolean =>
  status === ImportStatus.Completed || status === ImportStatus.PartiallyCompleted;

export interface ImportErrorRow {
  key: string;
  row: number | null;
  column: string | null;
  message: string;
}

/** Errors of a job for the table: the column header instead of its key; row 0 is about the whole file. */
export const toErrorRows = (job: Pick<ImportJobDetails, 'errors' | 'columnNames'>): ImportErrorRow[] =>
  job.errors.map((error: ImportJobError, index) => ({
    key: `${error.row}-${error.column ?? ''}-${index}`,
    row: error.row > 0 ? error.row : null,
    column: error.column ? (job.columnNames[error.column] ?? error.column) : null,
    message: error.message,
  }));
