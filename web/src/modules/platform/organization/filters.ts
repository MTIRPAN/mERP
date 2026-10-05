/** Active/inactive filter of the organization lists. */
export const activeFilter = ['active', 'inactive', 'all'] as const;

export type ActiveFilter = (typeof activeFilter)[number];

export const toActiveParam = (filter?: ActiveFilter): boolean | undefined =>
  filter === 'active' ? true : filter === 'inactive' ? false : undefined;
