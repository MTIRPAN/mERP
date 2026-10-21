import { EmploymentStatus } from '@/shared/api/generated/model';

/** Custom field entity of employees (Merp.Modules.HR.Application.HrCustomFieldEntities). */
export const EMPLOYEE_ENTITY = 'hr.employee';

export const statusFilters = ['active', 'upcoming', 'terminated', 'all'] as const;

export type StatusFilter = (typeof statusFilters)[number];

/** The list's status parameter: who works today unless another state is chosen (D38). */
export const toStatusParam = (filter: StatusFilter | undefined): EmploymentStatus[] | undefined => {
  switch (filter) {
    case 'upcoming':
      return [EmploymentStatus.Upcoming];
    case 'terminated':
      return [EmploymentStatus.Terminated];
    case 'all':
      return Object.values(EmploymentStatus);
    default:
      return undefined;
  }
};
