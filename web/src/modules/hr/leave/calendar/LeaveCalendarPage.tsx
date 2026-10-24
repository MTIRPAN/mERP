import type { DatesSetArg } from '@fullcalendar/core';
import trLocale from '@fullcalendar/core/locales/tr';
import dayGridPlugin from '@fullcalendar/daygrid';
import listPlugin from '@fullcalendar/list';
import FullCalendar from '@fullcalendar/react';
import { Alert, Card, Checkbox, Flex, Select, Space, Switch, Tag, TreeSelect } from 'antd';
import dayjs from 'dayjs';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { useGetLeaveCalendar, useGetOrgUnitTree } from '@/shared/api/generated/endpoints/hr/hr';
import type { LeaveCategory } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { formatDate } from '@/shared/format/date';
import { buildUnitTree, toUnitOptions } from '../../organization/unitTree';
import { hrPermissions } from '../../permissions';
import { categoryColors, toCalendarEvents } from './leaveCalendar';

const ALERTS_SHOWN = 5;

/**
 * The leave calendar (PLAN L5, D45): approved leaves and those waiting for approval of the people in scope, by
 * company, unit or the user's team, over the public holidays; a month and a list view, coloured by kind of leave. Days
 * on which a unit has more people away than the company's threshold are listed above.
 */
export const LeaveCalendarPage = () => {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const me = useMe();
  const canOpenRequests = useHasPermission(hrPermissions.leaveRequestsRead);
  const [range, setRange] = useState(() => ({
    from: dayjs().startOf('month').format('YYYY-MM-DD'),
    to: dayjs().endOf('month').format('YYYY-MM-DD'),
  }));
  const [companyId, setCompanyId] = useState<string>();
  const [orgUnitId, setOrgUnitId] = useState<string>();
  const [includeSubUnits, setIncludeSubUnits] = useState(true);
  const [myTeam, setMyTeam] = useState(false);
  const [includePending, setIncludePending] = useState(true);
  const tree = useGetOrgUnitTree({ companyId }, { query: { enabled: !!companyId } });
  const unitOptions = useMemo(() => toUnitOptions(buildUnitTree(tree.data?.units ?? [])), [tree.data]);
  const calendar = useGetLeaveCalendar({ ...range, companyId, orgUnitId, includeSubUnits, myTeam, includePending });
  const events = useMemo(() => toCalendarEvents(calendar.data, t), [calendar.data, t]);
  const alerts = calendar.data?.alerts ?? [];

  const onDatesSet = (dates: DatesSetArg) =>
    setRange({
      from: dayjs(dates.start).format('YYYY-MM-DD'),
      // FullCalendar's end is exclusive
      to: dayjs(dates.end).subtract(1, 'day').format('YYYY-MM-DD'),
    });

  return (
    <>
      <PageHeader title={t('web.hr.leave_calendar.title')} subtitle={t('web.hr.leave_calendar.subtitle')} />
      <Flex vertical gap={16}>
        <Card size="small">
          <Flex gap={12} wrap align="center">
            <Select
              aria-label={t('web.hr.leave_calendar.company')}
              allowClear
              placeholder={t('web.hr.leave_calendar.all_companies')}
              value={companyId}
              onChange={(value?: string) => {
                setCompanyId(value);
                setOrgUnitId(undefined);
              }}
              options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
              style={{ minWidth: 200 }}
            />
            <TreeSelect
              aria-label={t('web.hr.leave_calendar.org_unit')}
              allowClear
              showSearch
              disabled={!companyId}
              placeholder={t('web.hr.leave_calendar.org_unit')}
              treeNodeFilterProp="title"
              treeDefaultExpandAll
              loading={tree.isLoading}
              treeData={unitOptions}
              value={orgUnitId}
              onChange={(value?: string) => setOrgUnitId(value)}
              style={{ minWidth: 240 }}
            />
            <Checkbox
              checked={includeSubUnits}
              disabled={!orgUnitId}
              onChange={(e) => setIncludeSubUnits(e.target.checked)}
            >
              {t('web.hr.leave_calendar.include_sub_units')}
            </Checkbox>
            <Space>
              <Switch
                size="small"
                checked={myTeam}
                onChange={setMyTeam}
                aria-label={t('web.hr.leave_calendar.my_team')}
              />
              {t('web.hr.leave_calendar.my_team')}
            </Space>
            <Space>
              <Switch
                size="small"
                checked={includePending}
                onChange={setIncludePending}
                aria-label={t('web.hr.leave_calendar.include_pending')}
              />
              {t('web.hr.leave_calendar.include_pending')}
            </Space>
          </Flex>
        </Card>
        {alerts.length > 0 && (
          <Alert
            type="warning"
            showIcon
            title={t('web.hr.leave_calendar.alerts_title')}
            description={
              <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                {alerts.slice(0, ALERTS_SHOWN).map((a) => (
                  <li key={`${a.date}-${a.orgUnitId}`}>
                    {t('web.hr.leave_calendar.alert', {
                      date: formatDate(a.date, i18n.language),
                      unit: a.orgUnitName,
                      count: a.count,
                      threshold: a.threshold,
                    })}
                  </li>
                ))}
                {alerts.length > ALERTS_SHOWN && (
                  <li>{t('web.hr.leave_calendar.more_alerts', { count: alerts.length - ALERTS_SHOWN })}</li>
                )}
              </ul>
            }
          />
        )}
        <Card size="small">
          <Flex gap={6} wrap style={{ marginBottom: 12 }}>
            {(Object.keys(categoryColors) as LeaveCategory[])
              .filter((c) => c !== 'Arrangement' && c !== 'Travel')
              .map((category) => (
                <Tag key={category} color={categoryColors[category]}>
                  {t(`web.hr.leave_calendar.categories.${category}`)}
                </Tag>
              ))}
          </Flex>
          <FullCalendar
            plugins={[dayGridPlugin, listPlugin]}
            initialView="dayGridMonth"
            locales={[trLocale]}
            locale={i18n.language.startsWith('tr') ? 'tr' : 'en'}
            headerToolbar={{ left: 'prev,next today', center: 'title', right: 'dayGridMonth,listMonth' }}
            buttonText={{
              today: t('web.hr.leave_calendar.today'),
              month: t('web.hr.leave_calendar.month'),
              list: t('web.hr.leave_calendar.list'),
            }}
            height="auto"
            dayMaxEvents={4}
            events={events}
            datesSet={onDatesSet}
            eventClick={(info) => {
              if (canOpenRequests && !info.event.id.startsWith('holiday-')) {
                void navigate(`/hr/leave-requests/${info.event.id}`);
              }
            }}
          />
        </Card>
      </Flex>
    </>
  );
};
