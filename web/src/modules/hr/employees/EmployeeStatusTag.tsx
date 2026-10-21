import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import type { EmploymentStatus } from '@/shared/api/generated/model';

const colors: Record<EmploymentStatus, string> = { Upcoming: 'processing', Active: 'success', Terminated: 'default' };

/** State of an employment on its company's day (D38). */
export const EmployeeStatusTag = ({ status }: { status: EmploymentStatus }) => {
  const { t } = useTranslation();
  return (
    <Tag color={colors[status]} variant="filled">
      {t(`web.hr.employment_status.${status}`)}
    </Tag>
  );
};
