import { Card } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { PageHeader } from '@/shared/components/PageHeader';
import { ApprovalRequestView } from './ApprovalRequestView';

/** A request on its own page; notifications link here (/approvals/requests/{id}). */
export const ApprovalRequestPage = () => {
  const { t } = useTranslation();
  const { id } = useParams<{ id: string }>();

  return (
    <>
      <PageHeader title={t('web.approvals.request.title')} />
      <Card style={{ maxWidth: 820 }}>{id && <ApprovalRequestView requestId={id} />}</Card>
    </>
  );
};
