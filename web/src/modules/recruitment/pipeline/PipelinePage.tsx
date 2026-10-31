import { ArrowLeftOutlined } from '@ant-design/icons';
import { Alert, App, Button, Input, Segmented, Select, Skeleton, Space, Tag, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import {
  getListApplicationsQueryKey,
  getListMyHiringApplicationsQueryKey,
  listApplications,
  listMyHiringApplications,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { ApplicationListItem, ApplicationStatus, PipelineStage } from '@/shared/api/generated/model';
import type { ListParams } from '@/shared/api/paging';
import { isApiError } from '@/shared/api/problem';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatMomentDate } from '@/shared/format/date';
import { OpeningStatusTag } from '../openings/OpeningDetailDrawer';
import { stageTypeColors } from '../settings/stages';
import { ApplicationDrawer, ApplicationStatusTag } from './ApplicationDrawer';
import { KanbanBoard } from './KanbanBoard';
import { applicationStatuses, useApplicationActions, useOpeningApplications, usePipeline } from './pipeline';
import { ReasonModal } from './ReasonModal';

interface Leaving {
  kind: 'reject' | 'withdraw';
  application: ApplicationListItem;
}

/**
 * The applications of an opening by stage (PLAN R4a, D50): a kanban to drag them through the pipeline, or a list.
 * <c>team</c>: the hiring manager and recruiters of the opening read it from self service.
 */
export const PipelinePage = ({ team = false }: { team?: boolean }) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { id = '' } = useParams();
  const pipeline = usePipeline(id, team);
  const applications = useOpeningApplications(id, team);
  const actions = useApplicationActions(id);
  const [view, setView] = useState<'kanban' | 'list'>('kanban');
  const [open, setOpen] = useState<string>();
  const [leaving, setLeaving] = useState<Leaving>();
  const [rejectingRest, setRejectingRest] = useState(false);
  const [statuses, setStatuses] = useState<ApplicationStatus[]>([]);
  const [search, setSearch] = useState<string>();
  const items = useMemo(() => applications.data?.items ?? [], [applications.data]);
  const data = pipeline.data;
  // Once every seat is hired for, the rest may be rejected at once; those waiting for HR stay (D54)
  const remaining = items.filter((a) => a.status === 'Active' && !a.hireRequestedAt).length;

  const report = (error: unknown) => {
    const fields = isApiError(error) ? (error.problem?.fieldErrors ?? []) : [];
    void message.error(
      fields.length > 0 ? fields.map((e) => e.message).join(' ') : isApiError(error) ? error.message : String(error),
    );
  };

  const dropped = (application: ApplicationListItem, stage: PipelineStage) => {
    if (stage.type === 'Rejected' || stage.type === 'Withdrawn') {
      setLeaving({ kind: stage.type === 'Rejected' ? 'reject' : 'withdraw', application });
      return;
    }

    // Hired takes the accepted offer (D53); without one the API says so
    void (stage.type === 'Hired' ? actions.hire(application.id) : actions.move(application.id, stage.id)).then(
      () =>
        message.success(
          stage.type === 'Hired'
            ? t('web.recruitment.pipeline.hire_requested', { name: application.candidateName })
            : t('web.recruitment.pipeline.moved_to', { name: application.candidateName, stage: stage.name }),
        ),
      report,
    );
  };

  const columns = useMemo<DataTableColumn<ApplicationListItem>[]>(
    () => [
      {
        key: 'name',
        title: t('web.recruitment.pipeline.fields.candidate'),
        sorter: true,
        render: (_, row) => <Typography.Link onClick={() => setOpen(row.id)}>{row.candidateName}</Typography.Link>,
      },
      { key: 'email', dataIndex: 'email', title: t('web.recruitment.pipeline.fields.email') },
      {
        key: 'stage',
        title: t('web.recruitment.pipeline.fields.stage'),
        render: (_, row) => (row.stageType ? <Tag color={stageTypeColors[row.stageType]}>{row.stageName}</Tag> : '—'),
      },
      {
        key: 'status',
        title: t('web.recruitment.pipeline.fields.status'),
        render: (_, row) => <ApplicationStatusTag status={row.status} />,
      },
      {
        key: 'appliedAt',
        title: t('web.recruitment.pipeline.fields.applied_at'),
        sorter: true,
        render: (_, row) => formatMomentDate(row.appliedAt, i18n.language),
      },
      {
        key: 'stageEnteredAt',
        title: t('web.recruitment.pipeline.fields.stage_entered_at'),
        sorter: true,
        render: (_, row) => formatMomentDate(row.stageEnteredAt, i18n.language),
      },
      {
        key: 'flags',
        title: t('web.recruitment.pipeline.fields.flags'),
        render: (_, row) => (
          <Space size={[4, 4]} wrap>
            {row.isInternal && <Tag color="blue">{t('web.recruitment.pipeline.internal')}</Tag>}
            {row.knockedOut && <Tag color="warning">{t('web.recruitment.pipeline.knocked_out')}</Tag>}
          </Space>
        ),
      },
    ],
    [t, i18n.language],
  );

  const fetchPage = (list: ListParams) => {
    const params = {
      openingId: id,
      page: list.page,
      pageSize: list.pageSize,
      sort: list.sort,
      status: list.filter?.status ? (list.filter.status.split(',') as ApplicationStatus[]) : undefined,
      search: list.filter?.search,
    };
    return team ? listMyHiringApplications(params) : listApplications(params);
  };

  if (!data) {
    return <Skeleton active />;
  }

  return (
    <>
      <PageHeader
        title={data.title}
        subtitle={
          <Space wrap>
            {data.companyName}
            <OpeningStatusTag status={data.status} />
          </Space>
        }
        extra={
          <Space wrap>
            <Button
              icon={<ArrowLeftOutlined />}
              onClick={() => void navigate(team ? '/recruitment/my-openings' : `/recruitment/openings/${id}`)}
            >
              {t(team ? 'web.recruitment.pipeline.back_to_mine' : 'web.recruitment.pipeline.back_to_opening')}
            </Button>
            <Segmented
              value={view}
              onChange={(value) => setView(value as 'kanban' | 'list')}
              options={[
                { value: 'kanban', label: t('web.recruitment.pipeline.views.kanban') },
                { value: 'list', label: t('web.recruitment.pipeline.views.list') },
              ]}
            />
          </Space>
        }
      />
      {!team && data.canManage && data.status === 'Filled' && remaining > 0 && (
        <Alert
          type="success"
          showIcon
          title={t('web.recruitment.pipeline.filled', { count: remaining })}
          action={
            <Button size="small" danger onClick={() => setRejectingRest(true)}>
              {t('web.recruitment.pipeline.reject_remaining', { count: remaining })}
            </Button>
          }
          style={{ marginBottom: 16 }}
        />
      )}
      {team && (
        <Alert type="info" showIcon title={t('web.recruitment.pipeline.team_hint')} style={{ marginBottom: 16 }} />
      )}
      {view === 'kanban' ? (
        <>
          {(applications.data?.totalCount ?? 0) > items.length && (
            <Alert
              type="warning"
              showIcon
              title={t('web.recruitment.pipeline.partial', {
                shown: items.length,
                total: applications.data?.totalCount,
              })}
              style={{ marginBottom: 16 }}
            />
          )}
          <KanbanBoard
            stages={data.stages}
            applications={items}
            canManage={data.canManage}
            onOpen={setOpen}
            onDrop={dropped}
          />
        </>
      ) : (
        <DataTable<ApplicationListItem>
          queryKey={[...(team ? getListMyHiringApplicationsQueryKey() : getListApplicationsQueryKey()), 'table', id]}
          fetchPage={fetchPage}
          columns={columns}
          rowKey="id"
          filter={{ status: statuses.length > 0 ? statuses.join(',') : undefined, search }}
          toolbar={
            <Space wrap>
              <Input.Search
                allowClear
                placeholder={t('web.recruitment.pipeline.search')}
                onSearch={(value) => setSearch(value.trim() || undefined)}
                style={{ width: 240 }}
              />
              <Select
                mode="multiple"
                allowClear
                aria-label={t('web.recruitment.pipeline.fields.status')}
                placeholder={t('web.recruitment.pipeline.all_statuses')}
                value={statuses}
                onChange={setStatuses}
                options={applicationStatuses.map((s) => ({
                  value: s,
                  label: t(`web.recruitment.pipeline.status.${s}`),
                }))}
                style={{ minWidth: 200 }}
              />
            </Space>
          }
        />
      )}
      {open && (
        <ApplicationDrawer
          id={open}
          pipeline={data}
          team={team}
          onClose={() => setOpen(undefined)}
          onMove={(stageId) => actions.move(open, stageId)}
          onReopen={() => actions.reopen(open)}
          onHire={() =>
            actions.hire(open).then(() =>
              message.success(
                t('web.recruitment.pipeline.hire_requested', {
                  name: items.find((a) => a.id === open)?.candidateName ?? '',
                }),
              ),
            )
          }
          onLeave={(kind, application) => setLeaving({ kind, application })}
        />
      )}
      {rejectingRest && (
        <ReasonModal
          kind="reject"
          candidateName=""
          title={t('web.recruitment.pipeline.reject_remaining', { count: remaining })}
          reasons={data.rejectionReasons}
          onClose={() => setRejectingRest(false)}
          onSubmit={(values) =>
            actions
              .rejectRemaining(values)
              .then((count) => message.success(t('web.recruitment.pipeline.rejected_remaining', { count })))
          }
        />
      )}
      {leaving && (
        <ReasonModal
          kind={leaving.kind}
          candidateName={leaving.application.candidateName}
          applicationId={leaving.application.id}
          reasons={leaving.kind === 'reject' ? data.rejectionReasons : data.withdrawalReasons}
          onClose={() => setLeaving(undefined)}
          onSubmit={(values) =>
            leaving.kind === 'reject'
              ? actions.reject(leaving.application.id, values)
              : actions.withdraw(leaving.application.id, values)
          }
        />
      )}
    </>
  );
};

/** The pipeline of an opening of the caller's hiring team, read only (D50). */
export const HiringPipelinePage = () => <PipelinePage team />;
