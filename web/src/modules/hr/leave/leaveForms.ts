import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import type { AnnualLeaveRules } from '@/shared/api/generated/model';
import { numberOrNull } from './leaveVersions';

/** The value of "every company" in the company select; the API takes null. */
export const ALL_COMPANIES = '';

/** The company's name, or "every company". */
export const useCompanyName = () => {
  const { t } = useTranslation();
  const me = useMe();
  return (companyId: string | null) =>
    companyId ? (me.companies.find((c) => c.id === companyId)?.name ?? '—') : t('web.hr.leave.all_companies');
};

/** The names as the API takes them: blank languages left out. */
export const toNames = (name: Record<string, string | undefined> | undefined) =>
  Object.fromEntries(
    Object.entries(name ?? {})
      .map(([culture, value]) => [culture, value?.trim() ?? ''] as const)
      .filter(([, value]) => value.length > 0),
  );

/** API errors of the name land on the default language. */
export const nameAliases = { name: ['name', DEFAULT_CULTURE] };

/** Annual leave rules as the API takes them: empty numbers become null, missing switches false. */
export const toRules = (rules: AnnualLeaveRules): AnnualLeaveRules => ({
  ...rules,
  carryOverLimitDays: numberOrNull(rules.carryOverLimitDays),
  minNoticeDays: numberOrNull(rules.minNoticeDays),
  maxDaysPerRequest: numberOrNull(rules.maxDaysPerRequest),
  tiers: rules.tiers ?? [],
  ageMinimums: (rules.ageMinimums ?? []).map((a) => ({
    ...a,
    maxAge: numberOrNull(a.maxAge),
    minAge: numberOrNull(a.minAge),
  })),
  extraDayRules: rules.extraDayRules ?? [],
  allowTravelDays: !!rules.allowTravelDays,
  useGroupSeniority: !!rules.useGroupSeniority,
  unpaidLeaveShiftsAccrualDate: !!rules.unpaidLeaveShiftsAccrualDate,
  allowBeforeFirstEntitlement: !!rules.allowBeforeFirstEntitlement,
  allowHalfDay: !!rules.allowHalfDay,
  allowHourly: !!rules.allowHourly,
  transferBalanceOnGroupTransfer: !!rules.transferBalanceOnGroupTransfer,
  allowPastDatesForHr: !!rules.allowPastDatesForHr,
});
