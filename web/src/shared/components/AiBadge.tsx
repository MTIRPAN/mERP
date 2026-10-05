import { StarFilled } from '@ant-design/icons';
import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';

/** Marks features whose results come from AI (D23, D24). */
export const AiBadge = () => {
  const { t } = useTranslation();
  return (
    <Tag variant="filled" color="purple" icon={<StarFilled />} style={{ marginInlineEnd: 0 }}>
      {t('web.ai.badge')}
    </Tag>
  );
};
