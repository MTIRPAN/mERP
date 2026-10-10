import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Card, Space, Table, Tag, Tooltip, Typography, type TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import { useListHolidayCalendars, useListWorkSchedules } from '@/shared/api/generated/endpoints/platform/platform';
import type { WorkScheduleItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { shortTime, weekdayNames } from './calendarFormat';
import { WorkScheduleDrawer } from './WorkScheduleDrawer';

/** Weekly working patterns of the tenant and its companies (PLAN P9, D33). */
export const WorkSchedulesPage = () => {
  const { t, i18n } = useTranslation();
  const canManage = useHasPermission(platformPermissions.calendarsManage);
  const schedules = useListWorkSchedules();
  const calendars = useListHolidayCalendars();
  const [drawer, setDrawer] = useState<{ open: boolean; schedule?: WorkScheduleItem }>({ open: false });
  const weekdays = weekdayNames(i18n.language, 'short');
  const number = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 });
  const calendarName = (id: string) => calendars.data?.find((c) => c.id === id)?.name ?? '—';

  const week = (row: WorkScheduleItem) => (
    <Space size={2} wrap>
      {row.days.map((day, index) => (
        <Tooltip
          key={index}
          title={
            day.isWorkingDay
              ? `${shortTime(day.start)}–${shortTime(day.end)}${
                  day.breakStart
                    ? ` (${t('web.calendars.break')} ${shortTime(day.breakStart)}–${shortTime(day.breakEnd)})`
                    : ''
                }`
              : t('web.calendars.day_off')
          }
        >
          <Tag color={day.isWorkingDay ? 'blue' : undefined} style={{ marginInlineEnd: 0 }}>
            {weekdays[index]}
          </Tag>
        </Tooltip>
      ))}
    </Space>
  );

  const columns: TableColumnsType<WorkScheduleItem> = [
    {
      key: 'code',
      title: t('web.platform.fields.code'),
      render: (_, row) => <Typography.Text code>{row.code}</Typography.Text>,
    },
    {
      key: 'name',
      title: t('web.calendars.name'),
      render: (_, row) => (
        <Space>
          {row.displayName}
          {row.isDefault && <Tag color="green">{t('web.calendars.default')}</Tag>}
        </Space>
      ),
    },
    {
      key: 'company',
      title: t('web.calendars.company'),
      render: (_, row) => row.companyName ?? t('web.calendars.all_companies'),
    },
    { key: 'week', title: t('web.calendars.week'), render: (_, row) => week(row) },
    {
      key: 'hours',
      title: t('web.calendars.weekly'),
      render: (_, row) =>
        t('web.calendars.weekly_summary', { days: row.workingDaysPerWeek, hours: number.format(row.weeklyHours) }),
    },
    { key: 'calendar', title: t('web.calendars.calendar'), render: (_, row) => calendarName(row.holidayCalendarId) },
    {
      key: 'status',
      title: t('web.platform.fields.status'),
      render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 56,
            render: (_: unknown, row: WorkScheduleItem) => (
              <Button
                type="text"
                icon={<EditOutlined />}
                aria-label={t('web.common.edit')}
                onClick={() => setDrawer({ open: true, schedule: row })}
              />
            ),
          },
        ]
      : []),
  ];

  return (
    <>
      <PageHeader
        title={t('web.calendars.schedules_title')}
        subtitle={t('web.calendars.schedules_subtitle')}
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.calendars.schedule_new')}
            </Button>
          )
        }
      />
      <Card>
        <Table<WorkScheduleItem>
          rowKey="id"
          columns={columns}
          dataSource={schedules.data ?? []}
          loading={schedules.isFetching}
          pagination={false}
          locale={{ emptyText: t('web.calendars.schedules_empty') }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      <WorkScheduleDrawer schedule={drawer.schedule} open={drawer.open} onClose={() => setDrawer({ open: false })} />
    </>
  );
};
