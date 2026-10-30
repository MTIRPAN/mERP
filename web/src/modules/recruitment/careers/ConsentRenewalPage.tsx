import { Button, Card, Flex, Popconfirm, Result, Skeleton, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { respondToConsentRenewal, useGetConsentRenewalPage } from '@/shared/api/generated/endpoints/consents/consents';
import { formatDate } from '@/shared/format/date';

/**
 * The candidate's page of the consent renewal link (PLAN R8, D55): the explicit consent text to renew so the data
 * stays in the pool, or a refusal. Anonymous; the link's token is the key and works once.
 */
export const ConsentRenewalPage = () => {
  const { t, i18n } = useTranslation();
  const { tenant = '', token = '' } = useParams();
  const page = useGetConsentRenewalPage(tenant, token, { query: { meta: { silent: true } } });
  const [answer, setAnswer] = useState<'renewed' | 'withdrawn'>();
  const [sending, setSending] = useState(false);
  const [failed, setFailed] = useState(false);

  const respond = async (accepted: boolean) => {
    setSending(true);
    setFailed(false);
    try {
      await respondToConsentRenewal(tenant, token, { accepted });
      setAnswer(accepted ? 'renewed' : 'withdrawn');
    } catch {
      setFailed(true);
    } finally {
      setSending(false);
    }
  };

  if (answer) {
    return (
      <div className="merp-careers">
        <Result status="success" title={t(`web.recruitment.consent_renewal.${answer}`)} />
      </div>
    );
  }

  if (page.isError) {
    return (
      <div className="merp-careers">
        <Result status="404" title={t('web.recruitment.consent_renewal.not_found')} />
      </div>
    );
  }

  if (!page.data) {
    return (
      <div className="merp-careers">
        <Skeleton active />
      </div>
    );
  }

  const data = page.data;
  return (
    <div className="merp-careers">
      <Typography.Title level={2}>{t('web.recruitment.consent_renewal.title')}</Typography.Title>
      <Typography.Paragraph>
        {t('web.recruitment.consent_renewal.intro', {
          name: data.firstName,
          date: formatDate(data.retentionEndsOn, i18n.language),
        })}
      </Typography.Paragraph>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 0 }}>{data.text}</Typography.Paragraph>
      </Card>
      {failed && (
        <Typography.Paragraph type="danger">{t('web.recruitment.consent_renewal.failed')}</Typography.Paragraph>
      )}
      <Flex gap={8} wrap>
        <Button type="primary" loading={sending} onClick={() => void respond(true)}>
          {t('web.recruitment.consent_renewal.accept')}
        </Button>
        <Popconfirm title={t('web.recruitment.consent_renewal.decline_confirm')} onConfirm={() => respond(false)}>
          <Button loading={sending}>{t('web.recruitment.consent_renewal.decline')}</Button>
        </Popconfirm>
      </Flex>
    </div>
  );
};
