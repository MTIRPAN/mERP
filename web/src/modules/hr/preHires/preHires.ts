import type { PreHireItem, PreHireStatus } from '@/shared/api/generated/model';

export const preHireStatuses: PreHireStatus[] = ['Pending', 'Completed', 'Cancelled'];

export const preHireStatusColors: Record<PreHireStatus, string> = {
  Pending: 'processing',
  Completed: 'success',
  Cancelled: 'default',
};

/** How HR completes a pre-hire (D54): a new assignment, a new employment of a known person, or a new person. */
export const completionOf = (preHire: PreHireItem): 'assignment' | 'rehire' | 'hire' =>
  preHire.openEmploymentId ? 'assignment' : preHire.existingEmployeeId ? 'rehire' : 'hire';
