import type { QueryClient } from '@tanstack/react-query';
import {
  getCountMyApprovalTasksQueryKey,
  getGetApprovalRequestQueryKey,
  getGetApprovalTimelineQueryKey,
  getListApprovalRequestsQueryKey,
  getListMyApprovalTasksQueryKey,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { ApprovalStatus } from '@/shared/api/generated/model';

/** After a decision or an administrator's action: the request, the inboxes and the badge reload. */
export const refreshApprovals = (queryClient: QueryClient, requestId?: string) =>
  Promise.all([
    requestId ? queryClient.invalidateQueries({ queryKey: getGetApprovalRequestQueryKey(requestId) }) : undefined,
    queryClient.invalidateQueries({ queryKey: [getGetApprovalTimelineQueryKey()[0]] }),
    queryClient.invalidateQueries({ queryKey: [getListMyApprovalTasksQueryKey()[0]] }),
    queryClient.invalidateQueries({ queryKey: getCountMyApprovalTasksQueryKey() }),
    queryClient.invalidateQueries({ queryKey: [getListApprovalRequestsQueryKey()[0]] }),
  ]);

/** Tag colours of request states. */
export const statusColors: Record<ApprovalStatus, string> = {
  Pending: 'processing',
  Approved: 'success',
  Rejected: 'error',
  Returned: 'warning',
  Cancelled: 'default',
  NeedsAttention: 'magenta',
};
