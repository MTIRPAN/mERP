import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import {
  getGetRequisitionQueryKey,
  getListRequisitionPositionsQueryKey,
  getListRequisitionsQueryKey,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { RequisitionEmploymentType, RequisitionReason, RequisitionStatus } from '@/shared/api/generated/model';

/** Mirrors RecruitmentDocumentTypes.Requisition: the approval document type of requisitions (D48). */
export const REQUISITION_DOCUMENT = 'recruitment.requisition';

export const requisitionStatuses: RequisitionStatus[] = [
  'Draft',
  'Pending',
  'Returned',
  'Approved',
  'Rejected',
  'Cancelled',
  'Filled',
];

export const requisitionReasons: RequisitionReason[] = ['NewHeadcount', 'Replacement'];

export const employmentTypes: RequisitionEmploymentType[] = [
  'Indefinite',
  'FixedTerm',
  'PartTime',
  'Seasonal',
  'Intern',
];

export const statusColors: Record<RequisitionStatus, string> = {
  Draft: 'default',
  Pending: 'processing',
  Returned: 'warning',
  Approved: 'success',
  Rejected: 'error',
  Cancelled: 'default',
  Filled: 'cyan',
};

/** The value once it stopped changing for <c>delay</c> ms, for searches as the user types. */
export const useSettled = <T>(value: T, delay = 250) => {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return settled;
};

/** Refreshes the list, the seats of the positions and a requisition after a change. */
export const useRequisitionRefresh = () => {
  const queryClient = useQueryClient();
  return (id?: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getListRequisitionsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListRequisitionPositionsQueryKey() }),
      ...(id ? [queryClient.invalidateQueries({ queryKey: getGetRequisitionQueryKey(id) })] : []),
    ]);
};
