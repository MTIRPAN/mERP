import {
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  LeftOutlined,
  PlusOutlined,
  RightOutlined,
} from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App,
  Button,
  Card,
  Flex,
  Popconfirm,
  Select,
  Skeleton,
  Space,
  Table,
  Tag,
  Typography,
  type TableColumnsType,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getListHolidaysQueryKey,
  useCopyFixedHolidays,
  useListHolidayCalendars,
  useListHolidays,
  useRemoveHoliday,
} from '@/shared/api/generated/endpoints/platform/platform';
import { HolidayDuration, type HolidayItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { shortTime } from './calendarFormat';
import { HolidayDrawer } from './HolidayDrawer';
import { HolidayYearView } from './HolidayYearView';

interface DrawerState {
  open: boolean;
  holiday?: HolidayItem;
  date?: string;
}

/** Public holidays of a calendar, one year at a time (PLAN P9, D33). */
export const HolidaysPage = () => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.calendarsManage);
  const calendars = useListHolidayCalendars();
  const [selected, setSelected] = useState<string>();
  const [year, setYear] = useState(() => new Date().getFullYear());
  const calendarId = selected ?? calendars.data?.[0]?.id;
  const holidays = useListHolidays(calendarId ?? '', { year }, { query: { enabled: !!calendarId } });
  const remove = useRemoveHoliday();
  const copy = useCopyFixedHolidays();
  const [drawer, setDrawer] = useState<DrawerState>({ open: false });

  if (!calendars.data) {
    return <Skeleton active />;
  }

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: getListHolidaysQueryKey(calendarId ?? '') });
    await queryClient.invalidateQueries({ queryKey: ['/api/platform/work-schedules'] });
  };

  const removeHoliday = async (holiday: HolidayItem) => {
    await remove.mutateAsync({ id: calendarId ?? '', holidayId: holiday.id });
    await refresh();
  };

  const copyFixed = async () => {
    const copied = await copy.mutateAsync({ id: calendarId ?? '', data: { year } });
    await refresh();
    void message.success(t('web.calendars.copied', { count: copied.count, year: year - 1 }));
  };

  const dateText = (date: string) =>
    new Intl.DateTimeFormat(i18n.language, {
      day: 'numeric',
      month: 'long',
      weekday: 'long',
      timeZone: 'UTC',
    }).format(new Date(`${date}T00:00:00Z`));

  const columns: TableColumnsType<HolidayItem> = [
    { key: 'date', title: t('web.calendars.date'), render: (_, row) => dateText(row.date) },
    { key: 'name', title: t('web.calendars.name'), render: (_, row) => row.displayName },
    {
      key: 'duration',
      title: t('web.calendars.duration'),
      render: (_, row) =>
        row.duration === HolidayDuration.Half ? (
          <Tag color="warning">{t('web.calendars.half_day_at', { time: shortTime(row.startsAt) })}</Tag>
        ) : (
          <Tag color="error">{t('web.calendars.full_day')}</Tag>
        ),
    },
    {
      key: 'movable',
      title: t('web.calendars.kind'),
      render: (_, row) => t(row.isMovable ? 'web.calendars.kind_movable' : 'web.calendars.kind_fixed'),
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 96,
            render: (_: unknown, row: HolidayItem) => (
              <Space size={0}>
                <Button
                  type="text"
                  icon={<EditOutlined />}
                  aria-label={t('web.common.edit')}
                  onClick={() => setDrawer({ open: true, holiday: row })}
                />
                <Popconfirm title={t('web.calendars.remove_confirm')} onConfirm={() => removeHoliday(row)}>
                  <Button type="text" danger icon={<DeleteOutlined />} aria-label={t('web.common.delete')} />
                </Popconfirm>
              </Space>
            ),
          },
        ]
      : []),
  ];

  const items = holidays.data?.items ?? [];

  return (
    <>
      <PageHeader
        title={t('web.calendars.holidays_title')}
        subtitle={t('web.calendars.holidays_subtitle')}
        extra={
          canManage &&
          calendarId && (
            <Space wrap>
              <Popconfirm title={t('web.calendars.copy_confirm', { from: year - 1, to: year })} onConfirm={copyFixed}>
                <Button icon={<CopyOutlined />} loading={copy.isPending}>
                  {t('web.calendars.copy_fixed', { year: year - 1 })}
                </Button>
              </Popconfirm>
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
                {t('web.calendars.holiday_new')}
              </Button>
            </Space>
          )
        }
      />
      <Flex vertical gap={16}>
        <Card size="small">
          <Flex justify="space-between" align="center" wrap gap={12}>
            <Select
              style={{ minWidth: 220 }}
              value={calendarId}
              onChange={setSelected}
              aria-label={t('web.calendars.calendar')}
              options={calendars.data.map((c) => ({ value: c.id, label: c.name }))}
            />
            <Space>
              <Button
                icon={<LeftOutlined />}
                aria-label={t('web.calendars.previous_year')}
                onClick={() => setYear(year - 1)}
              />
              <Typography.Title level={4} style={{ margin: 0, minWidth: 64, textAlign: 'center' }}>
                {year}
              </Typography.Title>
              <Button
                icon={<RightOutlined />}
                aria-label={t('web.calendars.next_year')}
                onClick={() => setYear(year + 1)}
              />
            </Space>
          </Flex>
        </Card>
        {holidays.data && !holidays.data.hasMovableHolidays && (
          <Alert
            type="warning"
            showIcon
            title={t('web.calendars.missing_movable', { year })}
            description={t('web.calendars.missing_movable_hint')}
          />
        )}
        {holidays.isLoading ? (
          <Skeleton active />
        ) : (
          <HolidayYearView
            year={year}
            holidays={items}
            onSelect={canManage ? (date, holiday) => setDrawer({ open: true, holiday, date }) : undefined}
          />
        )}
        <Card title={t('web.calendars.list_title', { year })}>
          <Table<HolidayItem>
            rowKey="id"
            columns={columns}
            dataSource={items}
            loading={holidays.isFetching}
            pagination={false}
            locale={{ emptyText: t('web.calendars.empty', { year }) }}
            scroll={{ x: 'max-content' }}
          />
        </Card>
      </Flex>
      {calendarId && (
        <HolidayDrawer
          calendarId={calendarId}
          holiday={drawer.holiday}
          date={drawer.date}
          open={drawer.open}
          onClose={() => setDrawer({ open: false })}
        />
      )}
    </>
  );
};
