import { Select, Space, Tag, Typography } from 'antd';
import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { getListOffersQueryKey, listOffers } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { OfferListItem, OfferStatus } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { ForbiddenPage } from '@/shared/pages/ForbiddenPage';
import { formatDate, formatDateTime } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { OfferDrawer, OfferStatusTag } from './OfferDrawer';
import { offerStatuses } from './offers';

/**
 * Job offers (PLAN R6, D53) of the companies in scope; offers are made from an application's drawer.
 * <c>/recruitment/offers/:id</c> (the approval inbox's link) opens one.
 */
export const OffersPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams();
  const activeCompanyId = useActiveCompanyId();
  const [statuses, setStatuses] = useState<OfferStatus[]>([]);

  const open = useCallback(
    (offerId: string | undefined) => void navigate(offerId ? `/recruitment/offers/${offerId}` : '/recruitment/offers'),
    [navigate],
  );

  const columns = useMemo<DataTableColumn<OfferListItem>[]>(
    () => [
      {
        key: 'number',
        title: t('web.recruitment.offers.fields.number'),
        render: (_, row) => <Typography.Link onClick={() => open(row.id)}>{row.number}</Typography.Link>,
      },
      { key: 'candidateName', dataIndex: 'candidateName', title: t('web.recruitment.offers.fields.candidate') },
      { key: 'positionTitle', dataIndex: 'positionTitle', title: t('web.recruitment.offers.fields.position') },
      {
        key: 'openingTitle',
        dataIndex: 'openingTitle',
        title: t('web.recruitment.offers.fields.opening'),
        hiddenByDefault: true,
      },
      {
        key: 'status',
        title: t('web.recruitment.offers.fields.status'),
        render: (_, row) => (
          <Space size={4} wrap>
            <OfferStatusTag status={row.status} />
            {row.isAboveBudget && <Tag color="warning">{t('web.recruitment.offers.above_budget')}</Tag>}
          </Space>
        ),
      },
      {
        key: 'startDate',
        title: t('web.recruitment.offers.fields.start_date'),
        render: (_, row) => formatDate(row.startDate, i18n.language),
      },
      {
        key: 'expiresOn',
        title: t('web.recruitment.offers.fields.expires_on'),
        sorter: true,
        render: (_, row) => formatDate(row.expiresOn, i18n.language),
      },
      {
        key: 'sentAt',
        title: t('web.recruitment.offers.fields.sent_at'),
        hiddenByDefault: true,
        render: (_, row) => (row.sentAt ? formatDateTime(row.sentAt, i18n.language) : '—'),
      },
    ],
    [t, i18n.language, open],
  );

  const fetchPage = (list: ListParams) =>
    listOffers({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      status: list.filter?.status ? (list.filter.status.split(',') as OfferStatus[]) : undefined,
    });

  return (
    <>
      <PageHeader title={t('web.recruitment.offers.title')} subtitle={t('web.recruitment.offers.subtitle')} />
      <DataTable<OfferListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListOffersQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ status: statuses.length > 0 ? statuses.join(',') : undefined }}
        toolbar={
          <Select
            mode="multiple"
            allowClear
            aria-label={t('web.recruitment.offers.fields.status')}
            placeholder={t('web.recruitment.offers.all_statuses')}
            value={statuses}
            onChange={setStatuses}
            options={offerStatuses.map((s) => ({ value: s, label: t(`web.recruitment.offers.status.${s}`) }))}
            style={{ minWidth: 240 }}
          />
        }
      />
      {id && <OfferDrawer id={id} onClose={() => open(undefined)} />}
    </>
  );
};

/**
 * The link of an offer in the approval inbox: those who read offers see it in the list; the opening's hiring manager,
 * usually its first approver, reads it from self service (D53).
 */
export const OfferLinkPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id = '' } = useParams();
  const readsOffers = useHasPermission(recruitmentPermissions.offersRead);
  const hires = useHasPermission(recruitmentPermissions.selfHiring);

  if (readsOffers) {
    return <OffersPage />;
  }

  if (!hires) {
    return <ForbiddenPage />;
  }

  return (
    <>
      <PageHeader title={t('web.recruitment.offers.title')} subtitle={t('web.recruitment.offers.team_subtitle')} />
      <OfferDrawer id={id} team onClose={() => void navigate('/')} />
    </>
  );
};
