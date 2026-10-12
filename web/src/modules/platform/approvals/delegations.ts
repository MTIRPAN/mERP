import type { DelegationItem } from '@/shared/api/generated/model';

export type DelegationState = 'active' | 'upcoming' | 'ended' | 'revoked';

/** Where a delegation stands on `today` (yyyy-MM-dd); both dates are inclusive. */
export const delegationState = (
  delegation: Pick<DelegationItem, 'from' | 'to' | 'isRevoked'>,
  today: string,
): DelegationState =>
  delegation.isRevoked ? 'revoked' : today < delegation.from ? 'upcoming' : today > delegation.to ? 'ended' : 'active';
