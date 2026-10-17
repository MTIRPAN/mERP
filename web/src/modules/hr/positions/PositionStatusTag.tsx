import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import { PositionStatus } from '@/shared/api/generated/model';

const colors: Record<PositionStatus, string> = {
  [PositionStatus.Active]: 'success',
  [PositionStatus.Frozen]: 'processing',
  [PositionStatus.Closed]: 'default',
};

/** State of a position (kadro durumu). */
export const PositionStatusTag = ({ status }: { status: PositionStatus }) => {
  const { t } = useTranslation();
  return (
    <Tag color={colors[status]} variant="filled">
      {t(`web.hr.position_status.${status}`)}
    </Tag>
  );
};
