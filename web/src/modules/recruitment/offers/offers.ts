import { useQueryClient } from '@tanstack/react-query';
import {
  getGetApplicationQueryKey,
  getGetOfferQueryKey,
  getListOffersQueryKey,
  useGetMyHiringOffer,
  useGetOffer,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { OfferStatus, SalaryPeriod } from '@/shared/api/generated/model';

/** The approvable document type of offers, for the approval timeline (D53). */
export const OFFER_DOCUMENT = 'recruitment.offer';

export const CURRENCIES = ['TRY', 'USD', 'EUR'];

export const salaryPeriods: SalaryPeriod[] = ['Monthly', 'Yearly'];

export const offerStatuses: OfferStatus[] = [
  'Draft',
  'Pending',
  'Returned',
  'Approved',
  'Rejected',
  'Sent',
  'Accepted',
  'Declined',
  'Expired',
  'Withdrawn',
];

export const offerStatusColors: Record<OfferStatus, string> = {
  Draft: 'default',
  Pending: 'processing',
  Returned: 'warning',
  Approved: 'cyan',
  Rejected: 'error',
  Sent: 'blue',
  Accepted: 'success',
  Declined: 'volcano',
  Expired: 'default',
  Withdrawn: 'default',
};

/** An amount with two decimals and its currency code, e.g. "90.000,00 TRY". */
export const formatMoney = (amount: number, currency: string, locale: string) =>
  `${new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)} ${currency}`;

/**
 * An offer: the recruiters' endpoint, or from self service the hiring manager's (D53). Both hooks are called so the
 * rules of hooks hold; only one fetches.
 */
export const useOfferDetails = (id: string, team: boolean) => {
  const recruiter = useGetOffer(id, { query: { enabled: !team } });
  const member = useGetMyHiringOffer(id, { query: { enabled: team } });
  return team ? member : recruiter;
};

/** Refreshes the offer lists, an offer and its application after a change. */
export const useOfferRefresh = () => {
  const queryClient = useQueryClient();
  return (offerId: string | undefined, applicationId: string) =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getListOffersQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getGetApplicationQueryKey(applicationId) }),
      ...(offerId ? [queryClient.invalidateQueries({ queryKey: getGetOfferQueryKey(offerId) })] : []),
    ]);
};
