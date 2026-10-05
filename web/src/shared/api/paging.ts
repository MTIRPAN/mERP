/** List contract (CLAUDE.md): page, pageSize (max 200), sort, filter → PagedResult. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ListParams {
  page: number;
  pageSize: number;
  /** Field name; a leading "-" sorts descending. */
  sort?: string;
  filter?: Record<string, string | undefined>;
}

export const MAX_PAGE_SIZE = 200;
