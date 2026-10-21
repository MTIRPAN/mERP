import { AssignmentReason } from '@/shared/api/generated/model';
import type { AssignmentValues } from './AssignmentFields';
import { day, text } from './formValues';
import type { TermsValues } from './TermsFields';

/** Reasons a user picks for an assignment change; "Hire" belongs to hiring and rehiring only (D38). */
export const changeReasons = Object.values(AssignmentReason).filter((reason) => reason !== AssignmentReason.Hire);

/** The SGK leaving code of a transfer within the group (nakil), offered by default. */
export const TRANSFER_EXIT_CODE = '16';

/** Assignment fields of a lifecycle command. */
export const toAssignment = (values: AssignmentValues) => ({
  positionId: values.positionId ?? '',
  branchId: values.branchId ?? null,
  managerPositionId: values.managerPositionId ?? null,
  level: text(values.level),
  costCenterCode: text(values.costCenterCode),
});

/** Employment terms of a lifecycle command starting on <paramref name="start" /> (YYYY-MM-DD). */
export const toTerms = (values: TermsValues, start: string | null, withSeniority = true) => {
  const seniorityDate = withSeniority ? day(values.seniorityDate) : null;
  const differs = !!seniorityDate && seniorityDate !== start;
  return {
    employmentType: values.employmentType ?? null,
    contractEndDate: values.employmentType === 'FixedTerm' ? day(values.contractEndDate) : null,
    weeklyHours: values.employmentType === 'PartTime' ? (values.weeklyHours ?? null) : null,
    probationEndDate: day(values.probationEndDate),
    seniorityDate: differs ? seniorityDate : null,
    seniorityReason: differs ? text(values.seniorityReason) : null,
  };
};
