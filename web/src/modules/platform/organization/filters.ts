/** Active/inactive filter of the organization lists. */
export const activeFilter = ['active', 'inactive', 'all'] as const;

export type ActiveFilter = (typeof activeFilter)[number];

export const toActiveParam = (filter?: ActiveFilter): boolean | undefined =>
  filter === 'active' ? true : filter === 'inactive' ? false : undefined;

/** Custom field entity type of companies (PLAN P6). */
export const COMPANY_ENTITY = 'platform.company';
