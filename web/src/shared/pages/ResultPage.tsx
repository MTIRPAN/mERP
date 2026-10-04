import { Button, Result, type ResultProps } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';

interface ResultPageProps {
  status: ResultProps['status'];
  titleKey: string;
  descriptionKey: string;
  action?: ReactNode;
}

export const ResultPage = ({ status, titleKey, descriptionKey, action }: ResultPageProps) => {
  const { t } = useTranslation();
  return (
    <Result
      status={status}
      title={t(titleKey)}
      subTitle={t(descriptionKey)}
      extra={
        action ?? (
          <Link to="/">
            <Button type="primary">{t('web.common.back_home')}</Button>
          </Link>
        )
      }
    />
  );
};
