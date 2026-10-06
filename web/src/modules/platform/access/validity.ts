import dayjs, { type Dayjs } from 'dayjs';

/** DateOnly wire format of the API. */
const DATE_ONLY = 'YYYY-MM-DD';

export const toDateOnly = (value?: Dayjs | null): string | null => (value ? value.format(DATE_ONLY) : null);

export const fromDateOnly = (value?: string | null): Dayjs | undefined => (value ? dayjs(value) : undefined);
