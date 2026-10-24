import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import type { LeaveRequestStatus } from '@/shared/api/generated/model';
import { statusColors } from './leaveRequests';

export const LeaveRequestStatusTag = ({ status }: { status: LeaveRequestStatus }) => {
  const { t } = useTranslation();
  return <Tag color={statusColors[status]}>{t(`web.hr.leave_requests.status.${status}`)}</Tag>;
};
