import { Card, Col, Row, Tooltip, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { HolidayDuration, type HolidayItem } from '@/shared/api/generated/model';
import { isoDate, monthName, monthWeeks, shortTime, weekdayNames } from './calendarFormat';

interface HolidayYearViewProps {
  year: number;
  holidays: HolidayItem[];
  /** Called with the clicked day; undefined makes the days plain text. */
  onSelect?: (date: string, holiday?: HolidayItem) => void;
}

/** Twelve month grids, Monday first; full holidays are filled, half days are filled half way. */
export const HolidayYearView = ({ year, holidays, onSelect }: HolidayYearViewProps) => {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const byDate = new Map(holidays.map((h) => [h.date, h]));
  const weekdays = weekdayNames(i18n.language, 'narrow');

  const background = (holiday?: HolidayItem) => {
    if (!holiday) {
      return undefined;
    }

    return holiday.duration === HolidayDuration.Full
      ? token.colorErrorBg
      : `linear-gradient(135deg, transparent 50%, ${token.colorWarningBg} 50%)`;
  };

  const title = (holiday: HolidayItem) =>
    holiday.duration === HolidayDuration.Half
      ? t('web.calendars.half_day_from', { name: holiday.displayName, time: shortTime(holiday.startsAt) })
      : holiday.displayName;

  return (
    <Row gutter={[12, 12]}>
      {Array.from({ length: 12 }, (_, i) => i + 1).map((month) => (
        <Col key={month} xs={24} sm={12} lg={8} xl={6}>
          <Card size="small" title={monthName(i18n.language, month)}>
            <table style={{ width: '100%', borderCollapse: 'collapse', tableLayout: 'fixed', textAlign: 'center' }}>
              <thead>
                <tr>
                  {weekdays.map((day, index) => (
                    <th key={index} style={{ fontWeight: 'normal', color: token.colorTextTertiary, fontSize: 12 }}>
                      {day}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {monthWeeks(year, month).map((week, w) => (
                  <tr key={w}>
                    {week.map((day, d) => {
                      if (day === null) {
                        return <td key={d} />;
                      }

                      const date = isoDate(year, month, day);
                      const holiday = byDate.get(date);
                      const cell = (
                        <Typography.Text
                          type={!holiday && d >= 5 ? 'secondary' : undefined}
                          strong={!!holiday}
                          style={{ fontSize: 12 }}
                        >
                          {day}
                        </Typography.Text>
                      );

                      return (
                        <td
                          key={d}
                          data-date={date}
                          data-holiday={holiday?.duration}
                          aria-label={holiday ? title(holiday) : undefined}
                          onClick={onSelect ? () => onSelect(date, holiday) : undefined}
                          style={{
                            padding: '2px 0',
                            borderRadius: token.borderRadiusSM,
                            background: background(holiday),
                            cursor: onSelect ? 'pointer' : undefined,
                          }}
                        >
                          {holiday ? <Tooltip title={title(holiday)}>{cell}</Tooltip> : cell}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </Col>
      ))}
    </Row>
  );
};
