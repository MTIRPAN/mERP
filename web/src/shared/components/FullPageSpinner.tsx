import { Flex, Spin } from 'antd';
import { useTranslation } from 'react-i18next';

export const FullPageSpinner = ({ labelKey = 'web.common.loading' }: { labelKey?: string }) => {
  const { t } = useTranslation();
  return (
    <Flex align="center" justify="center" style={{ minHeight: '60vh' }}>
      <Spin size="large" description={t(labelKey)} />
    </Flex>
  );
};
