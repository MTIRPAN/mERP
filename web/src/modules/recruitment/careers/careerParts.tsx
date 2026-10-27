import { EnvironmentOutlined } from '@ant-design/icons';
import { Space, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { CareerOpeningItem } from '@/shared/api/generated/model';
import { formatDate } from '@/shared/format/date';

/** Company, place, work model and employment type of an opening, as candidates see them. */
export const OpeningFacts = ({ opening }: { opening: CareerOpeningItem }) => {
  const { t, i18n } = useTranslation();
  return (
    <Space size={[8, 4]} wrap>
      <Typography.Text strong>{opening.companyName}</Typography.Text>
      {opening.branchName && (
        <Typography.Text type="secondary">
          <EnvironmentOutlined /> {opening.branchName}
        </Typography.Text>
      )}
      <Tag>{t(`web.recruitment.openings.work_models.${opening.workModel}`)}</Tag>
      <Tag>{t(`web.recruitment.requisitions.employment_types.${opening.employmentType}`)}</Tag>
      {opening.visibility === 'Internal' && <Tag color="purple">{t('web.recruitment.careers.internal')}</Tag>}
      {opening.publishUntil && (
        <Typography.Text type="secondary">
          {t('web.recruitment.careers.until', { date: formatDate(opening.publishUntil, i18n.language) })}
        </Typography.Text>
      )}
    </Space>
  );
};
