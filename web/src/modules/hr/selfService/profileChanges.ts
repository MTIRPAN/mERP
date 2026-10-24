import type { ProfileChangeItem } from '@/shared/api/generated/model';

/** The approvable document type of change requests (D39). */
export const PROFILE_CHANGE_DOCUMENT = 'hr.profile-change';

/** Whether the request still waits for approval or for its requester's correction. */
export const isOpenProfileChange = (item: ProfileChangeItem) => item.status === 'Pending' || item.status === 'Returned';
