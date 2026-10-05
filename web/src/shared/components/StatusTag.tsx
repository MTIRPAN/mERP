import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';

const colors = {
  active: 'success',
  approved: 'success',
  inactive: 'default',
  draft: 'default',
  pending: 'processing',
  rejected: 'error',
  cancelled: 'warning',
} as const;

export type Status = keyof typeof colors;

/** Coloured status label; the text comes from web.status.{status}. */
export const StatusTag = ({ status }: { status: Status }) => {
  const { t } = useTranslation();
  return (
    <Tag color={colors[status]} variant="filled">
      {t(`web.status.${status}`)}
    </Tag>
  );
};
