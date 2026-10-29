import { PlusOutlined } from '@ant-design/icons';
import { Button, Flex, Input, Select, Space, Tooltip, Typography } from 'antd';
import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useActiveCompanyId } from '@/app/company/companyStore';
import {
  getListOpeningsQueryKey,
  listOpenings,
  useGetRequisition,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  OpeningListItem,
  OpeningStageCount,
  OpeningStatus,
  OpeningVisibility,
} from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { Can } from '@/shared/components/Can';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { OpeningDetailDrawer, OpeningStatusTag } from './OpeningDetailDrawer';
import { OpeningDrawer } from './OpeningDrawer';
import { openingStatuses, visibilities } from './openings';

/** The applications of an opening, the stages with some in a tooltip; a link to the pipeline for those who read it. */
const ApplicationCounts = ({ counts, onOpen }: { counts: OpeningStageCount[]; onOpen?: () => void }) => {
  const total = counts.reduce((sum, stage) => sum + stage.count, 0);
  if (total === 0) {
    return <Typography.Text type="secondary">0</Typography.Text>;
  }

  const tip = (
    <Flex vertical>
      {counts
        .filter((stage) => stage.count > 0)
        .map((stage) => (
          <span key={stage.stageId}>
            {stage.name}: {stage.count}
          </span>
        ))}
    </Flex>
  );
  return (
    <Tooltip title={tip}>
      {onOpen ? <Typography.Link onClick={onOpen}>{total}</Typography.Link> : <span>{total}</span>}
    </Tooltip>
  );
};

/**
 * Job openings (PLAN R3a, D49) of the companies in scope, with filters and a new opening.
 * <c>/recruitment/openings/:id</c> opens one; <c>?fromRequisition=…</c> (the requisition's "Create opening") opens
 * the form filled from an approved requisition.
 */
export const OpeningsPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const fromRequisition = searchParams.get('fromRequisition') ?? undefined;
  const activeCompanyId = useActiveCompanyId();
  const [statuses, setStatuses] = useState<OpeningStatus[]>([]);
  const [visibility, setVisibility] = useState<OpeningVisibility>();
  const [search, setSearch] = useState<string>();
  const [creating, setCreating] = useState(false);
  const requisition = useGetRequisition(fromRequisition ?? '', { query: { enabled: !!fromRequisition } });
  const readsApplications = useHasPermission(recruitmentPermissions.applicationsRead);

  const open = useCallback(
    (openingId: string | undefined) =>
      void navigate(openingId ? `/recruitment/openings/${openingId}` : '/recruitment/openings'),
    [navigate],
  );

  const columns = useMemo<DataTableColumn<OpeningListItem>[]>(
    () => [
      {
        key: 'title',
        title: t('web.recruitment.openings.fields.title'),
        sorter: true,
        render: (_, row) => <Typography.Link onClick={() => open(row.id)}>{row.title}</Typography.Link>,
      },
      { key: 'companyName', dataIndex: 'companyName', title: t('web.recruitment.openings.fields.company') },
      { key: 'branchName', dataIndex: 'branchName', title: t('web.recruitment.openings.fields.branch') },
      {
        key: 'workModel',
        title: t('web.recruitment.openings.fields.work_model'),
        render: (_, row) => t(`web.recruitment.openings.work_models.${row.workModel}`),
      },
      { key: 'headcount', dataIndex: 'headcount', title: t('web.recruitment.openings.fields.headcount') },
      {
        key: 'applications',
        title: t('web.recruitment.openings.fields.applications'),
        render: (_, row) => (
          <ApplicationCounts
            counts={row.stageCounts}
            onOpen={readsApplications ? () => void navigate(`/recruitment/openings/${row.id}/pipeline`) : undefined}
          />
        ),
      },
      {
        key: 'visibility',
        title: t('web.recruitment.openings.fields.visibility'),
        render: (_, row) => t(`web.recruitment.openings.visibilities.${row.visibility}`),
      },
      {
        key: 'status',
        title: t('web.recruitment.openings.fields.status'),
        render: (_, row) => <OpeningStatusTag status={row.status} />,
      },
      {
        key: 'publishUntil',
        title: t('web.recruitment.openings.fields.publish_until'),
        sorter: true,
        render: (_, row) => (row.publishUntil ? formatDate(row.publishUntil, i18n.language) : '—'),
      },
      {
        key: 'requisitionNumber',
        dataIndex: 'requisitionNumber',
        title: t('web.recruitment.openings.fields.requisition'),
        hiddenByDefault: true,
      },
      {
        key: 'hiringManagerName',
        dataIndex: 'hiringManagerName',
        title: t('web.recruitment.openings.fields.hiring_manager'),
        hiddenByDefault: true,
      },
    ],
    [t, i18n.language, open, navigate, readsApplications],
  );

  const fetchPage = (list: ListParams) =>
    listOpenings({
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      status: list.filter?.status ? (list.filter.status.split(',') as OpeningStatus[]) : undefined,
      visibility: list.filter?.visibility as OpeningVisibility | undefined,
      search: list.filter?.search,
    });

  const closeForm = () => {
    setCreating(false);
    if (fromRequisition) {
      searchParams.delete('fromRequisition');
      setSearchParams(searchParams, { replace: true });
    }
  };

  return (
    <>
      <PageHeader
        title={t('web.recruitment.openings.title')}
        subtitle={t('web.recruitment.openings.subtitle')}
        extra={
          <Can permission={recruitmentPermissions.openingsManage}>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('web.recruitment.openings.new_title')}
            </Button>
          </Can>
        }
      />
      <DataTable<OpeningListItem>
        // The active company is sent as X-Company-Id; it is part of the key so the list refreshes when it changes
        queryKey={[...getListOpeningsQueryKey(), activeCompanyId]}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="id"
        filter={{ status: statuses.length > 0 ? statuses.join(',') : undefined, visibility, search }}
        toolbar={
          <Space wrap>
            <Input.Search
              allowClear
              placeholder={t('web.recruitment.openings.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 240 }}
            />
            <Select
              mode="multiple"
              allowClear
              aria-label={t('web.recruitment.openings.fields.status')}
              placeholder={t('web.recruitment.openings.all_statuses')}
              value={statuses}
              onChange={setStatuses}
              options={openingStatuses.map((s) => ({ value: s, label: t(`web.recruitment.openings.status.${s}`) }))}
              style={{ minWidth: 200 }}
            />
            <Select
              allowClear
              aria-label={t('web.recruitment.openings.fields.visibility')}
              placeholder={t('web.recruitment.openings.all_visibilities')}
              value={visibility}
              onChange={setVisibility}
              options={visibilities.map((v) => ({ value: v, label: t(`web.recruitment.openings.visibilities.${v}`) }))}
              style={{ minWidth: 160 }}
            />
          </Space>
        }
      />
      {(creating || (fromRequisition && requisition.data)) && (
        <OpeningDrawer
          requisition={fromRequisition ? requisition.data : undefined}
          onClose={closeForm}
          onSaved={(saved) => open(saved)}
        />
      )}
      {id && <OpeningDetailDrawer id={id} onClose={() => open(undefined)} />}
    </>
  );
};
