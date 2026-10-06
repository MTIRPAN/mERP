import type { TFunction } from 'i18next';
import type { AssignmentItem } from '@/shared/api/generated/model';
import { formatDate } from '@/shared/format/date';

type Scoped = Pick<AssignmentItem, 'scopeTypeLabel' | 'scopeName' | 'includeSubUnits'>;

/** "Şirket: Acme (alt birimler dahil)" style text of an assignment's scope. */
export const scopeText = (assignment: Scoped, t: TFunction): string => {
  const scope = assignment.scopeName
    ? t('web.platform.assignments.scope_named', { type: assignment.scopeTypeLabel, name: assignment.scopeName })
    : assignment.scopeTypeLabel;
  return assignment.includeSubUnits ? t('web.platform.assignments.with_sub_units', { scope }) : scope;
};

type Period = Pick<AssignmentItem, 'validFrom' | 'validTo'>;

/** Validity period text; both ends are inclusive and either may be open. */
export const periodText = (assignment: Period, t: TFunction, locale: string): string => {
  const from = assignment.validFrom ? formatDate(assignment.validFrom, locale) : undefined;
  const to = assignment.validTo ? formatDate(assignment.validTo, locale) : undefined;
  if (from && to) {
    return t('web.platform.assignments.period_range', { from, to });
  }

  if (from) {
    return t('web.platform.assignments.period_from', { from });
  }

  return to ? t('web.platform.assignments.period_to', { to }) : t('web.platform.assignments.period_open');
};

export type AssignmentState = 'current' | 'future' | 'expired';

/** Whether the assignment grants its role today (the API decides with isCurrent), starts later or has ended. */
export const assignmentState = (
  assignment: Pick<AssignmentItem, 'isCurrent' | 'validFrom'>,
  today: string,
): AssignmentState =>
  assignment.isCurrent ? 'current' : assignment.validFrom && assignment.validFrom > today ? 'future' : 'expired';
