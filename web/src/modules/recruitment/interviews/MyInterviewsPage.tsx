import { CheckCircleTwoTone } from '@ant-design/icons';
import type { DatesSetArg } from '@fullcalendar/core';
import trLocale from '@fullcalendar/core/locales/tr';
import dayGridPlugin from '@fullcalendar/daygrid';
import listPlugin from '@fullcalendar/list';
import FullCalendar from '@fullcalendar/react';
import { Card, Flex, Segmented, Select, Space, Switch, Table, Typography } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useListInterviews } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { InterviewListItem, InterviewStatus } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDateTime } from '@/shared/format/date';
import { recruitmentPermissions } from '../permissions';
import { InterviewDrawer, InterviewStatusTag } from './InterviewDrawer';
import { toCalendarEvents } from './interviews';

const statuses: InterviewStatus[] = ['Scheduled', 'Completed', 'Cancelled', 'NoShow'];

/** The calendar loads one page of a month's interviews. */
const CALENDAR_PAGE_SIZE = 200;

type View = 'list' | 'calendar';

/**
 * My interviews (PLAN R5, D52): the interviews I interview in, as a list or on a calendar, with my evaluation form a
 * click away. Those who manage interviews may see every interview of their companies too.
 */
export const MyInterviewsPage = () => {
  const { t, i18n } = useTranslation();
  const canManage = useHasPermission(recruitmentPermissions.interviewsManage);
  const [view, setView] = useState<View>('list');
  const [mine, setMine] = useState(true);
  const [status, setStatus] = useState<InterviewStatus[]>(['Scheduled']);
  const [page, setPage] = useState({ page: 1, pageSize: 20 });
  const [range, setRange] = useState<{ from: string; to: string }>();
  const [open, setOpen] = useState<string>();
  const list = useListInterviews({ ...page, mine, status, sort: 'startsAt' }, { query: { enabled: view === 'list' } });
  const calendar = useListInterviews(
    { pageSize: CALENDAR_PAGE_SIZE, mine, from: range?.from, to: range?.to, sort: 'startsAt' },
    { query: { enabled: view === 'calendar' && !!range } },
  );
  const events = useMemo(() => toCalendarEvents(calendar.data?.items ?? []), [calendar.data]);

  const onDatesSet = (dates: DatesSetArg) => setRange({ from: dates.start.toISOString(), to: dates.end.toISOString() });

  return (
    <>
      <PageHeader
        title={t('web.recruitment.interviews.my_title')}
        subtitle={t('web.recruitment.interviews.my_subtitle')}
      />
      <Flex vertical gap={16}>
        <Card size="small">
          <Flex gap={12} wrap align="center">
            <Segmented<View>
              value={view}
              onChange={setView}
              options={[
                { value: 'list', label: t('web.recruitment.interviews.views.list') },
                { value: 'calendar', label: t('web.recruitment.interviews.views.calendar') },
              ]}
            />
            {view === 'list' && (
              <Select
                aria-label={t('web.recruitment.interviews.fields.status')}
                mode="multiple"
                allowClear
                placeholder={t('web.recruitment.interviews.all_statuses')}
                value={status}
                onChange={(value: InterviewStatus[]) => {
                  setStatus(value);
                  setPage((p) => ({ ...p, page: 1 }));
                }}
                options={statuses.map((s) => ({ value: s, label: t(`web.recruitment.interviews.status.${s}`) }))}
                style={{ minWidth: 240 }}
              />
            )}
            {canManage && (
              <Space>
                <Switch
                  size="small"
                  checked={mine}
                  onChange={setMine}
                  aria-label={t('web.recruitment.interviews.only_mine')}
                />
                {t('web.recruitment.interviews.only_mine')}
              </Space>
            )}
          </Flex>
        </Card>
        {view === 'list' ? (
          <Table<InterviewListItem>
            rowKey="id"
            size="small"
            loading={list.isLoading}
            dataSource={list.data?.items ?? []}
            onRow={(row) => ({ onClick: () => setOpen(row.id), style: { cursor: 'pointer' } })}
            pagination={{
              current: page.page,
              pageSize: page.pageSize,
              total: list.data?.totalCount ?? 0,
              onChange: (current, pageSize) => setPage({ page: current, pageSize }),
            }}
            columns={[
              {
                key: 'when',
                title: t('web.recruitment.interviews.fields.when'),
                render: (_, row) => formatDateTime(row.startsAt, i18n.language),
              },
              { key: 'candidate', title: t('web.recruitment.interviews.fields.candidate'), dataIndex: 'candidateName' },
              { key: 'opening', title: t('web.recruitment.interviews.fields.opening'), dataIndex: 'openingTitle' },
              {
                key: 'type',
                title: t('web.recruitment.interviews.fields.type'),
                render: (_, row) => t(`web.recruitment.interviews.types.${row.type}`),
              },
              {
                key: 'interviewers',
                title: t('web.recruitment.interviews.fields.interviewers'),
                render: (_, row) => row.interviewers.map((p) => p.name ?? '—').join(', '),
              },
              {
                key: 'status',
                title: t('web.recruitment.interviews.fields.status'),
                render: (_, row) => <InterviewStatusTag status={row.status} />,
              },
              {
                key: 'feedback',
                title: t('web.recruitment.interviews.fields.feedback'),
                render: (_, row) => (
                  <Space>
                    {row.myFeedbackSent && (
                      <CheckCircleTwoTone
                        twoToneColor="#52c41a"
                        aria-label={t('web.recruitment.interviews.feedback.mine_sent')}
                      />
                    )}
                    <Typography.Text type="secondary">
                      {t('web.recruitment.interviews.feedback.count', {
                        count: row.feedbackCount,
                        total: row.interviewers.length,
                      })}
                    </Typography.Text>
                  </Space>
                ),
              },
            ]}
          />
        ) : (
          <Card size="small">
            <FullCalendar
              plugins={[dayGridPlugin, listPlugin]}
              initialView="dayGridMonth"
              locales={[trLocale]}
              locale={i18n.language.startsWith('tr') ? 'tr' : 'en'}
              headerToolbar={{ left: 'prev,next today', center: 'title', right: 'dayGridMonth,listWeek' }}
              buttonText={{
                today: t('web.recruitment.interviews.calendar.today'),
                month: t('web.recruitment.interviews.calendar.month'),
                list: t('web.recruitment.interviews.calendar.week'),
              }}
              dayMaxEvents={4}
              events={events}
              datesSet={onDatesSet}
              eventClick={(info) => setOpen(info.event.id)}
              height="auto"
            />
          </Card>
        )}
      </Flex>
      {open && <InterviewDrawer id={open} onClose={() => setOpen(undefined)} />}
    </>
  );
};
