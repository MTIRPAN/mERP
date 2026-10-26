import { useQueryClient } from '@tanstack/react-query';
import { App, Card, Divider, Drawer, Empty, Input, List, Skeleton, Table, Tabs, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getListMyApplicationsQueryKey,
  submitMyApplication,
  useGetMyOpening,
  useListMyApplications,
  useListMyOpenings,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { MyApplicationItem } from '@/shared/api/generated/model';
import { PageHeader } from '@/shared/components/PageHeader';
import { RichTextView } from '@/shared/components/RichTextEditor';
import { formatDateTime } from '@/shared/format/date';
import { ApplicationForm } from './ApplicationForm';
import { OpeningFacts } from './careerParts';
import { matches } from './careers';

/** An opening with its texts and the form; the employee applies as an internal candidate. */
const OpeningDrawer = ({ id, applied, onClose }: { id: string; applied: boolean; onClose: () => void }) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const { data: details } = useGetMyOpening(id, { culture: i18n.language });

  return (
    <Drawer title={details?.opening.title ?? t('web.recruitment.careers.title')} open onClose={onClose} size={720}>
      {!details ? (
        <Skeleton active />
      ) : (
        <>
          <OpeningFacts opening={details.opening} />
          <Divider />
          <RichTextView html={details.description} />
          {details.qualifications && (
            <>
              <Typography.Title level={5}>{t('web.recruitment.careers.qualifications')}</Typography.Title>
              <RichTextView html={details.qualifications} />
            </>
          )}
          <Card title={t('web.recruitment.careers.apply_title')} style={{ marginTop: 16 }}>
            {applied ? (
              <Typography.Text type="success">{t('web.recruitment.careers.already_applied')}</Typography.Text>
            ) : (
              <ApplicationForm
                details={details}
                anonymous={false}
                culture={i18n.language}
                onSubmit={async (payload, cv) => {
                  await submitMyApplication(id, { application: JSON.stringify(payload), cv });
                  void message.success(t('web.recruitment.careers.sent_title'));
                  await queryClient.invalidateQueries({ queryKey: getListMyApplicationsQueryKey() });
                  onClose();
                }}
              />
            )}
          </Card>
        </>
      )}
    </Drawer>
  );
};

/**
 * Open positions for employees (PLAN R3b, D49): internal and public openings of the group, and the employee's own
 * applications. An application from here marks the candidate internal.
 */
export const MyOpeningsPage = () => {
  const { t, i18n } = useTranslation();
  const openings = useListMyOpenings();
  const applications = useListMyApplications();
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState<string>();
  const appliedTo = useMemo(() => new Set((applications.data ?? []).map((a) => a.openingId)), [applications.data]);
  const shown = (openings.data ?? []).filter((o) => matches(o, search));

  return (
    <>
      <PageHeader title={t('web.recruitment.careers.my_title')} subtitle={t('web.recruitment.careers.my_subtitle')} />
      <Tabs
        items={[
          {
            key: 'openings',
            label: t('web.recruitment.careers.tabs.openings'),
            children: (
              <>
                <Input.Search
                  allowClear
                  placeholder={t('web.recruitment.careers.search')}
                  onChange={(event) => setSearch(event.target.value)}
                  style={{ maxWidth: 360, marginBottom: 16 }}
                />
                <List
                  loading={openings.isLoading}
                  dataSource={shown}
                  locale={{ emptyText: <Empty description={t('web.recruitment.careers.no_openings')} /> }}
                  renderItem={(opening) => (
                    <List.Item key={opening.id}>
                      <Card size="small" className="merp-careers-card">
                        <Typography.Link onClick={() => setOpen(opening.id)} strong>
                          {opening.title}
                        </Typography.Link>
                        <OpeningFacts opening={opening} />
                        {appliedTo.has(opening.id) && (
                          <Typography.Text type="success">
                            {t('web.recruitment.careers.already_applied')}
                          </Typography.Text>
                        )}
                      </Card>
                    </List.Item>
                  )}
                />
              </>
            ),
          },
          {
            key: 'applications',
            label: t('web.recruitment.careers.tabs.applications'),
            children: (
              <Table<MyApplicationItem>
                rowKey="id"
                loading={applications.isLoading}
                dataSource={applications.data ?? []}
                pagination={false}
                locale={{ emptyText: <Empty description={t('web.recruitment.careers.no_applications')} /> }}
                columns={[
                  { key: 'opening', dataIndex: 'openingTitle', title: t('web.recruitment.careers.fields.opening') },
                  { key: 'company', dataIndex: 'companyName', title: t('web.recruitment.careers.fields.company') },
                  {
                    key: 'status',
                    title: t('web.recruitment.careers.fields.status'),
                    render: (_, row) => t(`web.recruitment.careers.application_status.${row.status}`),
                  },
                  {
                    key: 'appliedAt',
                    title: t('web.recruitment.careers.fields.applied_at'),
                    render: (_, row) => formatDateTime(row.appliedAt, i18n.language),
                  },
                ]}
              />
            ),
          },
        ]}
      />
      {open && <OpeningDrawer id={open} applied={appliedTo.has(open)} onClose={() => setOpen(undefined)} />}
    </>
  );
};
