import { ArrowLeftOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Flex,
  Popconfirm,
  Skeleton,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import {
  cancelOpeningScreening,
  requestOpeningScreening,
  useGetOpeningScreening,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type {
  CvRequirementStatus,
  ScreeningRow,
  ScreeningRowState,
  ScreeningStatus,
} from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime } from '@/shared/format/date';

const prefix = 'web.recruitment.screening';

const statusColors: Record<ScreeningStatus, string> = {
  Queued: 'default',
  Submitted: 'processing',
  Held: 'warning',
  Completed: 'success',
  Cancelled: 'default',
};

const stateColors: Record<ScreeningRowState, string> = {
  NoCv: 'default',
  NotAnalyzed: 'default',
  Current: 'success',
  Outdated: 'warning',
  Failed: 'error',
};

const requirementColors: Record<CvRequirementStatus, string> = {
  Met: 'success',
  Partial: 'gold',
  NotMet: 'error',
  Unclear: 'default',
};

/**
 * The batch screening of an opening (PLAN A6, D59): HR asks for it, the applications are analyzed at night, and the
 * table shows what each CV shows for each requirement. Applications stay in the order they came: no score, no
 * ranking, no rejection.
 */
export const ScreeningPage = () => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { id = '' } = useParams();
  const { data, refetch } = useGetOpeningScreening(id);
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await action();
      await refetch();
    } catch (error) {
      void message.error(isApiError(error) ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  };

  if (!data) {
    return <Skeleton active />;
  }

  const screening = data.screening;
  const columns: TableColumnsType<ScreeningRow> = [
    {
      key: 'candidate',
      title: t(`${prefix}.columns.candidate`),
      fixed: 'left',
      width: 200,
      render: (_, row) => <Link to={`/recruitment/candidates/${row.candidateId}`}>{row.candidateName}</Link>,
    },
    {
      key: 'applied',
      title: t(`${prefix}.columns.applied`),
      width: 160,
      render: (_, row) => formatDateTime(row.appliedAt, i18n.language),
    },
    { key: 'stage', title: t(`${prefix}.columns.stage`), width: 140, render: (_, row) => row.stage ?? '—' },
    {
      key: 'state',
      title: t(`${prefix}.columns.state`),
      width: 150,
      render: (_, row) => <Tag color={stateColors[row.state]}>{t(`${prefix}.states.${row.state}`)}</Tag>,
    },
    ...data.requirements.map((requirement) => ({
      key: `requirement-${requirement}`,
      title: requirement,
      width: 180,
      render: (_: unknown, row: ScreeningRow) => {
        const cell = row.cells.find((c) => c.requirement === requirement);
        return cell ? (
          <Tooltip title={cell.evidence}>
            <Tag color={requirementColors[cell.status]}>{t(`${prefix}.requirements.${cell.status}`)}</Tag>
          </Tooltip>
        ) : (
          <Typography.Text type="secondary">—</Typography.Text>
        );
      },
    })),
  ];

  return (
    <>
      <PageHeader
        title={
          <Space>
            {t(`${prefix}.title`, { position: data.openingTitle })}
            <AiBadge />
          </Space>
        }
        extra={
          <Space wrap>
            <Button icon={<ArrowLeftOutlined />} onClick={() => void navigate(`/recruitment/openings/${id}/pipeline`)}>
              {t(`${prefix}.back`)}
            </Button>
            {data.canRequest && (
              <Button type="primary" loading={busy} onClick={() => void run(() => requestOpeningScreening(id))}>
                {t(`${prefix}.request`)}
              </Button>
            )}
          </Space>
        }
      />
      <Flex vertical gap={16}>
        <Alert type="info" showIcon title={t(`${prefix}.notice`)} />
        {!data.enabled && <Alert type="warning" showIcon title={t(`${prefix}.disabled`)} />}
        {data.enabled && !data.hasRequirements && (
          <Alert type="warning" showIcon title={t(`${prefix}.no_requirements`)} />
        )}
        <Card
          size="small"
          title={t(`${prefix}.status_title`)}
          extra={
            screening?.canCancel && (
              <Popconfirm
                title={t(`${prefix}.cancel_confirm`)}
                onConfirm={() => run(() => cancelOpeningScreening(id, screening.id))}
              >
                <Button size="small" danger loading={busy}>
                  {t(`${prefix}.cancel`)}
                </Button>
              </Popconfirm>
            )
          }
        >
          <Typography.Paragraph style={{ marginBottom: screening ? 12 : 0 }}>
            {t(`${prefix}.due`, { count: data.due })}
          </Typography.Paragraph>
          {screening ? (
            <Descriptions
              size="small"
              column={{ xs: 1, md: 2 }}
              items={[
                {
                  key: 'status',
                  label: t(`${prefix}.fields.status`),
                  children: (
                    <Space wrap>
                      <Tag color={statusColors[screening.status]}>{t(`${prefix}.statuses.${screening.status}`)}</Tag>
                      {screening.holdReason && (
                        <Typography.Text type="warning">
                          {t(`${prefix}.hold_reasons.${screening.holdReason.replace('common.errors.', '')}`, {
                            defaultValue: t(`${prefix}.hold_reasons.other`),
                          })}
                        </Typography.Text>
                      )}
                    </Space>
                  ),
                },
                {
                  key: 'requested',
                  label: t(`${prefix}.fields.requested`),
                  children: `${formatDateTime(screening.requestedAt, i18n.language)}${screening.requestedByName ? ` · ${screening.requestedByName}` : ''}`,
                },
                ...(screening.status === 'Submitted'
                  ? [
                      {
                        key: 'running',
                        label: t(`${prefix}.fields.running`),
                        children: t(`${prefix}.running`, { count: screening.running }),
                      },
                    ]
                  : []),
                {
                  key: 'analyzed',
                  label: t(`${prefix}.fields.analyzed`),
                  children: t(`${prefix}.analyzed`, { succeeded: screening.succeeded, failed: screening.failed }),
                },
                ...(screening.completedAt
                  ? [
                      {
                        key: 'completed',
                        label: t(`${prefix}.fields.completed`),
                        children: formatDateTime(screening.completedAt, i18n.language),
                      },
                    ]
                  : []),
              ]}
            />
          ) : (
            <Typography.Text type="secondary">{t(`${prefix}.none`)}</Typography.Text>
          )}
        </Card>
        <Table<ScreeningRow>
          rowKey="applicationId"
          size="small"
          columns={columns}
          dataSource={data.rows}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: t(`${prefix}.empty`) }}
        />
      </Flex>
    </>
  );
};
