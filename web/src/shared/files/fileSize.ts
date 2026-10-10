const units = ['byte', 'kilobyte', 'megabyte', 'gigabyte'] as const;

/** File size in the largest unit below 1024, e.g. "1,5 MB" in Turkish. */
export const formatFileSize = (bytes: number, locale: string): string => {
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: units[unit],
    unitDisplay: 'short',
    maximumFractionDigits: unit === 0 ? 0 : 1,
  }).format(value);
};
