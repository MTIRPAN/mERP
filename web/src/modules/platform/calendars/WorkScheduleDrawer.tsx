import { useQueryClient } from '@tanstack/react-query';
import { Col, Divider, Form, Input, Row, Select, Space, Switch, TimePicker, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import {
  getListWorkSchedulesQueryKey,
  useCreateWorkSchedule,
  useListHolidayCalendars,
  useUpdateWorkSchedule,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { WorkScheduleItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { weekdayNames } from './calendarFormat';
import { dayHours, standardWeek, toDayValues, toWorkDay, type WorkDayValues } from './workScheduleForm';

interface WorkScheduleValues {
  code?: string;
  name?: Record<string, string | undefined>;
  companyId?: string | null;
  holidayCalendarId?: string;
  days?: WorkDayValues[];
  isDefault?: boolean;
  isActive?: boolean;
}

interface WorkScheduleDrawerProps {
  /** Schedule to edit; undefined creates one. */
  schedule?: WorkScheduleItem;
  open: boolean;
  onClose: () => void;
}

/** Create or change a work schedule (PLAN P9, D33). The code and the company are fixed after creation. */
export const WorkScheduleDrawer = ({ schedule, open, onClose }: WorkScheduleDrawerProps) => {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const calendars = useListHolidayCalendars({ query: { enabled: open } });
  const create = useCreateWorkSchedule();
  const update = useUpdateWorkSchedule();
  const [form] = Form.useForm<WorkScheduleValues>();
  const days = Form.useWatch('days', form);
  const editing = !!schedule;
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const weekdays = weekdayNames(i18n.language);
  const formatter = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 });
  const hours = (index: number) => dayHours(toWorkDay(days?.[index]));
  const weeklyHours = Array.from({ length: 7 }, (_, i) => hours(i)).reduce((sum, h) => sum + h, 0);

  const submit = async (values: WorkScheduleValues) => {
    const name = Object.fromEntries(
      Object.entries(values.name ?? {}).flatMap(([culture, text]) => (text?.trim() ? [[culture, text.trim()]] : [])),
    );
    const fields = {
      name,
      holidayCalendarId: values.holidayCalendarId ?? '',
      days: Array.from({ length: 7 }, (_, i) => toWorkDay(values.days?.[i])),
      isDefault: values.isDefault ?? false,
    };

    if (schedule) {
      await update.mutateAsync({
        id: schedule.id,
        data: { ...fields, version: schedule.version, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({
        data: { ...fields, code: values.code?.trim() || null, companyId: values.companyId ?? null },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListWorkSchedulesQueryKey() });
    await queryClient.invalidateQueries({ queryKey: ['/api/platform/working-days'] });
  };

  const initial: WorkScheduleValues = schedule
    ? {
        code: schedule.code,
        name: schedule.name,
        companyId: schedule.companyId,
        holidayCalendarId: schedule.holidayCalendarId,
        days: schedule.days.map(toDayValues),
        isDefault: schedule.isDefault,
        isActive: schedule.isActive,
      }
    : {
        companyId: null,
        holidayCalendarId: calendars.data?.[0]?.id,
        days: standardWeek().map(toDayValues),
        isDefault: false,
        isActive: true,
      };

  // The API names a day by its index: days[0] is Monday
  const dayAliases = Object.fromEntries(
    Array.from({ length: 7 }, (_, i) => [`days[${i}]`, ['days', i, 'hours']] as const),
  ) as Record<string, (string | number)[]>;

  return (
    <FormDrawer<WorkScheduleValues>
      key={schedule ? `${schedule.id}-${schedule.version}` : `new-${calendars.data?.[0]?.id ?? ''}`}
      title={t(editing ? 'web.calendars.schedule_edit' : 'web.calendars.schedule_new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      initialValues={initial}
      width={760}
      fieldAliases={{ name: ['name', DEFAULT_CULTURE], days: ['days', 0, 'hours'], ...dayAliases }}
    >
      <Row gutter={12}>
        <Col xs={24} sm={8}>
          <Form.Item
            name="code"
            label={t('web.platform.fields.code')}
            tooltip={t('web.platform.fields.code_hint')}
            rules={required}
          >
            <Input maxLength={20} disabled={editing} style={{ textTransform: 'uppercase' }} />
          </Form.Item>
        </Col>
        <Col xs={24} sm={16}>
          <Form.Item name="companyId" label={t('web.calendars.company')} tooltip={t('web.calendars.company_hint')}>
            <Select
              allowClear
              disabled={editing}
              placeholder={t('web.calendars.all_companies')}
              showSearch={{ optionFilterProp: 'label' }}
              options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
            />
          </Form.Item>
        </Col>
      </Row>
      {me.cultures.map((culture) => (
        <Form.Item
          key={culture.code}
          name={['name', culture.code]}
          label={
            me.cultures.length > 1 ? t('web.calendars.name_in', { language: culture.name }) : t('web.calendars.name')
          }
          rules={culture.code === DEFAULT_CULTURE ? required : undefined}
        >
          <Input maxLength={100} />
        </Form.Item>
      ))}
      <Form.Item name="holidayCalendarId" label={t('web.calendars.calendar')} rules={required}>
        <Select
          loading={calendars.isLoading}
          options={calendars.data?.map((c) => ({ value: c.id, label: c.name })) ?? []}
        />
      </Form.Item>
      <Divider titlePlacement="start">{t('web.calendars.week')}</Divider>
      {weekdays.map((dayName, index) => {
        const working = days?.[index]?.isWorkingDay ?? false;
        return (
          <Row key={index} gutter={12} align="top" wrap>
            <Col xs={24} sm={6}>
              <Space style={{ paddingTop: 5, marginBottom: 8 }}>
                <Form.Item noStyle name={['days', index, 'isWorkingDay']} valuePropName="checked">
                  <Switch size="small" aria-label={dayName} />
                </Form.Item>
                <Typography.Text>{dayName}</Typography.Text>
              </Space>
            </Col>
            <Col xs={12} sm={7}>
              <Form.Item
                name={['days', index, 'hours']}
                rules={working ? [{ required: true, message: t('web.calendars.hours_required') }] : undefined}
                style={{ marginBottom: 8 }}
              >
                <TimePicker.RangePicker
                  format="HH:mm"
                  minuteStep={15}
                  disabled={!working}
                  placeholder={[t('web.calendars.start'), t('web.calendars.end')]}
                />
              </Form.Item>
            </Col>
            <Col xs={12} sm={7}>
              <Form.Item name={['days', index, 'break']} style={{ marginBottom: 8 }}>
                <TimePicker.RangePicker
                  format="HH:mm"
                  minuteStep={15}
                  disabled={!working}
                  placeholder={[t('web.calendars.break_start'), t('web.calendars.break_end')]}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={4} style={{ paddingTop: 5, textAlign: 'right' }}>
              <Typography.Text type="secondary">
                {working
                  ? t('web.calendars.hours', { hours: formatter.format(hours(index)) })
                  : t('web.calendars.day_off')}
              </Typography.Text>
            </Col>
          </Row>
        );
      })}
      <Typography.Paragraph strong style={{ textAlign: 'right' }}>
        {t('web.calendars.weekly_hours', { hours: formatter.format(weeklyHours) })}
      </Typography.Paragraph>
      <Divider />
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item
            name="isDefault"
            label={t('web.calendars.default')}
            extra={t('web.calendars.default_hint')}
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
        </Col>
        {editing && (
          <Col span={12}>
            <Form.Item name="isActive" label={t('web.platform.fields.active')} valuePropName="checked">
              <Switch disabled={schedule.isDefault} />
            </Form.Item>
          </Col>
        )}
      </Row>
    </FormDrawer>
  );
};
