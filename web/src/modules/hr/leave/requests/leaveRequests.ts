import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Dayjs } from 'dayjs';
import type { TFunction } from 'i18next';
import { useEffect, useState } from 'react';
import {
  getGetLeaveRequestQueryKey,
  getGetMyLeaveRequestQueryKey,
  getListLeaveRequestsQueryKey,
  getListMyLeaveBalancesQueryKey,
  getListMyLeaveRequestsQueryKey,
  previewLeaveRecord,
  previewLeaveRequest,
  previewMyLeaveRequest,
} from '@/shared/api/generated/endpoints/hr/hr';
import type {
  DurationBasis,
  LeaveRequestListItem,
  LeaveRequestPreview,
  LeaveRequestStatus,
  LeaveTypeOption,
} from '@/shared/api/generated/model';
import { formatDate } from '@/shared/format/date';

/** Approval document types of leave requests and their cancellations (D44). */
export const LEAVE_REQUEST_DOCUMENT = 'hr.leave-request';
export const LEAVE_CANCELLATION_DOCUMENT = 'hr.leave-cancellation';

/** Who fills the form: the employee, HR on their behalf, or HR recording an approved leave. */
export type LeaveFormMode = 'self' | 'onBehalf' | 'record';

export const statusColors: Record<LeaveRequestStatus, string> = {
  Draft: 'default',
  Pending: 'processing',
  Returned: 'warning',
  Approved: 'success',
  Rejected: 'error',
  Cancelled: 'default',
  CancellationPending: 'orange',
};

export const leaveRequestStatuses: LeaveRequestStatus[] = [
  'Draft',
  'Pending',
  'Returned',
  'Approved',
  'Rejected',
  'Cancelled',
  'CancellationPending',
];

/** Form values of a leave request; days and times are dayjs values until they are sent. */
export interface LeaveRequestValues {
  employmentId?: string;
  leaveTypeId?: string;
  startDate?: Dayjs;
  endDate?: Dayjs;
  startAfternoon?: boolean;
  endMorning?: boolean;
  startTime?: Dayjs;
  endTime?: Dayjs;
  eventDate?: Dayjs;
  travelDays?: number;
  note?: string;
  address?: string;
  phone?: string;
  substituteEmployeeId?: string;
}

/** The request body of the form: the type decides which fields count (D42, D44). */
export const toLeaveBody = (values: LeaveRequestValues, type: LeaveTypeOption | undefined) => {
  const date = (value: Dayjs | undefined) => value?.format('YYYY-MM-DD') ?? null;
  const time = (value: Dayjs | undefined) => value?.format('HH:mm:00') ?? null;
  const hourly = type?.durationBasis === 'Hours';
  const halfDay = type?.unit === 'HalfDay';
  return {
    leaveTypeId: values.leaveTypeId ?? null,
    startDate: date(values.startDate) ?? '',
    endDate: hourly ? date(values.startDate) : type?.hasFixedLength ? null : date(values.endDate ?? values.startDate),
    startPart: halfDay && values.startAfternoon ? ('Afternoon' as const) : ('Full' as const),
    endPart: halfDay && values.endMorning ? ('Morning' as const) : ('Full' as const),
    startTime: hourly ? time(values.startTime) : null,
    endTime: hourly ? time(values.endTime) : null,
    eventDate: type?.hasFixedLength ? date(values.eventDate) : null,
    travelDays: type?.category === 'Annual' ? (values.travelDays ?? 0) : 0,
    note: values.note?.trim() || null,
    address: values.address?.trim() || null,
    phone: values.phone?.trim() || null,
    substituteEmployeeId: values.substituteEmployeeId ?? null,
  };
};

export type LeaveBody = ReturnType<typeof toLeaveBody>;

/** "3,5 gün" or "2 saat". */
export const lengthText = (basis: DurationBasis, quantity: number, t: TFunction, language: string) =>
  t(basis === 'Hours' ? 'web.hr.leave_requests.length_hours' : 'web.hr.leave_requests.length_days', {
    quantity: quantity.toLocaleString(language),
  });

/** The days of a leave: one day or a range, with the half days and the hours of an hourly leave. */
export const periodText = (
  item: Pick<LeaveRequestListItem, 'startDate' | 'endDate' | 'startPart' | 'endPart' | 'startTime' | 'endTime'>,
  t: TFunction,
  language: string,
) => {
  const start = formatDate(item.startDate, language);
  const hours = item.startTime && item.endTime ? ` ${item.startTime.slice(0, 5)}–${item.endTime.slice(0, 5)}` : '';
  const from = `${start}${item.startPart === 'Afternoon' ? ` (${t('web.hr.leave_requests.afternoon')})` : ''}`;
  if (item.startDate === item.endDate) {
    return `${from}${item.endPart === 'Morning' ? ` (${t('web.hr.leave_requests.morning')})` : ''}${hours}`;
  }

  const to = `${formatDate(item.endDate, language)}${item.endPart === 'Morning' ? ` (${t('web.hr.leave_requests.morning')})` : ''}`;
  return `${from} – ${to}${hours}`;
};

/** A value that follows another after it stays still for a moment, for live previews. */
export const useSettled = <T>(value: T, delay = 400) => {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return settled;
};

/**
 * The live check of the form (D44): length, balance, problems, warnings and approvers. Runs once a type and a first
 * day are chosen; HR's previews also need the employment.
 */
export const useLeavePreview = (
  mode: LeaveFormMode,
  body: LeaveBody,
  extra: { employmentId?: string; requestId?: string },
) => {
  const key = useSettled(JSON.stringify({ ...body, ...extra }));
  const ready = !!body.leaveTypeId && !!body.startDate && (mode === 'self' || !!extra.employmentId);
  return useQuery<LeaveRequestPreview>({
    queryKey: ['leave-request-preview', mode, key],
    queryFn: () => {
      const request = JSON.parse(key) as LeaveBody & { employmentId?: string; requestId?: string };
      if (mode === 'self') {
        return previewMyLeaveRequest({ ...request, employmentId: null, requestId: request.requestId ?? null });
      }

      const withEmployment = { ...request, employmentId: request.employmentId ?? '' };
      return mode === 'record' ? previewLeaveRecord(withEmployment) : previewLeaveRequest(withEmployment);
    },
    enabled: ready,
    placeholderData: keepPreviousData,
    retry: false,
    meta: { silent: true },
  });
};

/** Reloads every list and balance a leave request change may touch. */
export const useLeaveRefresh = () => {
  const queryClient = useQueryClient();
  return (id?: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getListMyLeaveRequestsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListMyLeaveBalancesQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListLeaveRequestsQueryKey() }),
      // Every employment's balances on the employee cards
      queryClient.invalidateQueries({
        predicate: (query) => typeof query.queryKey[0] === 'string' && query.queryKey[0].endsWith('/leave-balances'),
      }),
      ...(id
        ? [
            queryClient.invalidateQueries({ queryKey: getGetMyLeaveRequestQueryKey(id) }),
            queryClient.invalidateQueries({ queryKey: getGetLeaveRequestQueryKey(id) }),
          ]
        : []),
    ]);
};
