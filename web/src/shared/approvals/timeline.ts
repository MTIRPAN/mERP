import type { TFunction } from 'i18next';
import type { ApprovalHistoryItem, ApprovalRequestStepItem } from '@/shared/api/generated/model';

/** Timeline detail of a system decision on an overdue step (ApprovalRequest.OverdueReason). */
export const OVERDUE = 'overdue';

/** Kinds whose detail is a comment written by a user. */
const COMMENTED = new Set<string>(['Approved', 'Rejected', 'Returned', 'ForceApproved']);

export type TimelineTone = 'success' | 'error' | 'warning' | 'info' | 'neutral';

export const timelineTone = (item: ApprovalHistoryItem): TimelineTone => {
  switch (item.kind) {
    case 'Approved':
    case 'ForceApproved':
    case 'Completed':
      return 'success';
    case 'Rejected':
    case 'NeedsAttention':
      return 'error';
    case 'Returned':
    case 'Escalated':
    case 'Reminded':
      return 'warning';
    case 'Submitted':
    case 'Resubmitted':
    case 'Assigned':
    case 'Reassigned':
      return 'info';
    default:
      return 'neutral';
  }
};

/** The sentence of a timeline entry, e.g. "Ayşe Yılmaz, Mehmet Demir adına onayladı". */
export const timelineText = (item: ApprovalHistoryItem, steps: ApprovalRequestStepItem[], t: TFunction): string => {
  const step = item.step !== null ? (steps[item.step]?.name ?? String(item.step + 1)) : '';
  const user = item.userName ?? '';
  const onBehalf = item.onBehalfOfName ?? '';
  const target = item.detailUserName ?? item.detail ?? '';
  const base = 'web.approvals.timeline';

  switch (item.kind) {
    case 'Submitted':
      return t(`${base}.submitted`, { user });
    case 'Resubmitted':
      return t(`${base}.resubmitted`, { user, round: item.round });
    case 'Assigned':
      return t(`${base}.assigned`, { user, step });
    case 'Skipped':
      return t(`${base}.skipped`, { step, reason: t(`${base}.skip_reasons.${item.detail ?? 'condition'}`) });
    case 'Approved':
    case 'Rejected':
      if (!item.userId) {
        return t(`${base}.${item.kind.toLowerCase()}_overdue`, { step });
      }

      return item.onBehalfOfUserId
        ? t(`${base}.${item.kind.toLowerCase()}_on_behalf`, { user, onBehalf, step })
        : t(`${base}.${item.kind.toLowerCase()}`, { user, step });
    case 'Returned':
      return item.onBehalfOfUserId
        ? t(`${base}.returned_on_behalf`, { user, onBehalf })
        : t(`${base}.returned`, { user });
    case 'Cancelled':
      return t(`${base}.cancelled`);
    case 'NeedsAttention':
      return t(`${base}.needs_attention.${item.detail ?? 'no_approver'}`, { step });
    case 'Reassigned':
      return t(`${base}.reassigned`, { user, from: onBehalf, to: target });
    case 'Escalated':
      return t(`${base}.escalated`, { from: onBehalf, to: target });
    case 'Reminded':
      return t(`${base}.reminded`, { user });
    case 'ForceApproved':
      return t(`${base}.force_approved`, { user, step });
    case 'Completed':
      return t(item.detail === 'no_definition' ? `${base}.completed_without_flow` : `${base}.completed`);
    default:
      return item.kind;
  }
};

/** A user's comment on the entry, if any; system reasons are not comments. */
export const timelineComment = (item: ApprovalHistoryItem): string | null =>
  COMMENTED.has(item.kind) && item.userId && item.detail ? item.detail : null;
