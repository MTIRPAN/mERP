import { useQueryClient } from '@tanstack/react-query';
import { DatePicker, Form, Input, Segmented, Switch, TimePicker } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import {
  getListHolidaysQueryKey,
  useAddHoliday,
  useUpdateHoliday,
} from '@/shared/api/generated/endpoints/platform/platform';
import { HolidayDuration, type HolidayItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { toApiTime, toPickerTime } from './calendarFormat';

interface HolidayFormValues {
  date?: Dayjs;
  name?: Record<string, string | undefined>;
  duration?: HolidayDuration;
  startsAt?: Dayjs;
  isMovable?: boolean;
}

interface HolidayDrawerProps {
  calendarId: string;
  /** Holiday to edit; undefined adds one. */
  holiday?: HolidayItem;
  /** Date of a new holiday, e.g. the day clicked in the year view. */
  date?: string;
  open: boolean;
  onClose: () => void;
}

/** Add or change a public holiday (PLAN P9). A half day starts at 13:00 unless another time is given. */
export const HolidayDrawer = ({ calendarId, holiday, date, open, onClose }: HolidayDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const add = useAddHoliday();
  const update = useUpdateHoliday();
  const [form] = Form.useForm<HolidayFormValues>();
  const duration = Form.useWatch('duration', form);
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: HolidayFormValues) => {
    const name = Object.fromEntries(
      Object.entries(values.name ?? {}).flatMap(([culture, text]) => (text?.trim() ? [[culture, text.trim()]] : [])),
    );
    const data = {
      date: values.date ? values.date.format('YYYY-MM-DD') : null,
      name,
      duration: values.duration ?? null,
      startsAt: values.duration === HolidayDuration.Half ? toApiTime(values.startsAt) : null,
      isMovable: values.isMovable ?? false,
    };
    if (holiday) {
      await update.mutateAsync({ id: calendarId, holidayId: holiday.id, data });
    } else {
      await add.mutateAsync({ id: calendarId, data });
    }

    // Holidays change the working days of every schedule
    await queryClient.invalidateQueries({ queryKey: getListHolidaysQueryKey(calendarId) });
    await queryClient.invalidateQueries({ queryKey: ['/api/platform/work-schedules'] });
  };

  const initial: HolidayFormValues = holiday
    ? {
        date: dayjs(holiday.date),
        name: holiday.name,
        duration: holiday.duration,
        startsAt: toPickerTime(holiday.startsAt),
        isMovable: holiday.isMovable,
      }
    : {
        date: date ? dayjs(date) : undefined,
        duration: HolidayDuration.Full,
        startsAt: toPickerTime('13:00:00'),
        isMovable: false,
      };

  return (
    <FormDrawer<HolidayFormValues>
      key={holiday?.id ?? date ?? 'new'}
      title={t(holiday ? 'web.calendars.holiday_edit' : 'web.calendars.holiday_new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      initialValues={initial}
      fieldAliases={{ name: ['name', DEFAULT_CULTURE] }}
    >
      <Form.Item name="date" label={t('web.calendars.date')} rules={required}>
        <DatePicker style={{ width: '100%' }} format="L" />
      </Form.Item>
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
      <Form.Item name="duration" label={t('web.calendars.duration')} rules={required}>
        <Segmented
          options={[
            { value: HolidayDuration.Full, label: t('web.calendars.full_day') },
            { value: HolidayDuration.Half, label: t('web.calendars.half_day') },
          ]}
        />
      </Form.Item>
      {duration === HolidayDuration.Half && (
        <Form.Item name="startsAt" label={t('web.calendars.starts_at')} extra={t('web.calendars.starts_at_hint')}>
          <TimePicker format="HH:mm" minuteStep={15} />
        </Form.Item>
      )}
      <Form.Item
        name="isMovable"
        label={t('web.calendars.movable')}
        extra={t('web.calendars.movable_hint')}
        valuePropName="checked"
      >
        <Switch />
      </Form.Item>
    </FormDrawer>
  );
};
