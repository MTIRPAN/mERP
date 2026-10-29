import { Button, Card, Descriptions, Flex, Input, Popconfirm, Result, Skeleton, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { respondToOffer, useGetOfferResponsePage } from '@/shared/api/generated/endpoints/offers/offers';
import { formatDate } from '@/shared/format/date';
import { formatMoney } from './offers';

/**
 * The candidate's page of an offer (PLAN R6, D53), opened with the link in the offer e-mail: the terms, and accept or
 * decline until the offer's last day. Anonymous; the link's token is the key.
 */
export const OfferResponsePage = () => {
  const { t, i18n } = useTranslation();
  const { tenant = '', token = '' } = useParams();
  const page = useGetOfferResponsePage(tenant, token, { query: { meta: { silent: true } } });
  const [note, setNote] = useState('');
  const [sending, setSending] = useState(false);
  const [failed, setFailed] = useState(false);

  const answer = async (accepted: boolean) => {
    setSending(true);
    setFailed(false);
    try {
      await respondToOffer(tenant, token, { accepted, note: note.trim() || null });
      await page.refetch();
    } catch {
      setFailed(true);
    } finally {
      setSending(false);
    }
  };

  if (page.isError) {
    return (
      <div className="merp-careers">
        <Result status="404" title={t('web.recruitment.offer_response.not_found')} />
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

  const offer = page.data;
  return (
    <div className="merp-careers">
      <Typography.Title level={2}>
        {t('web.recruitment.offer_response.title', { company: offer.companyName })}
      </Typography.Title>
      <Typography.Paragraph>
        {t('web.recruitment.offer_response.greeting', { name: offer.candidateName })}
      </Typography.Paragraph>
      <Descriptions
        bordered
        size="small"
        column={1}
        items={[
          { key: 'position', label: t('web.recruitment.offer_response.position'), children: offer.positionTitle },
          {
            key: 'start',
            label: t('web.recruitment.offer_response.start_date'),
            children: formatDate(offer.startDate, i18n.language),
          },
          {
            key: 'salary',
            label: t('web.recruitment.offer_response.salary'),
            children: `${formatMoney(offer.salary, offer.currency, i18n.language)} · ${t(`web.recruitment.offers.periods.${offer.period}`)}`,
          },
          ...(offer.benefits
            ? [
                {
                  key: 'benefits',
                  label: t('web.recruitment.offer_response.benefits'),
                  children: <span style={{ whiteSpace: 'pre-wrap' }}>{offer.benefits}</span>,
                },
              ]
            : []),
          {
            key: 'expires',
            label: t('web.recruitment.offer_response.expires_on'),
            children: formatDate(offer.expiresOn, i18n.language),
          },
        ]}
      />
      <Card style={{ marginTop: 24 }}>
        {offer.canRespond ? (
          <Flex vertical gap={12}>
            <Typography.Paragraph style={{ marginBottom: 0 }}>
              {t('web.recruitment.offer_response.intro')}
            </Typography.Paragraph>
            <Input.TextArea
              aria-label={t('web.recruitment.offer_response.note')}
              placeholder={t('web.recruitment.offer_response.note')}
              rows={3}
              maxLength={500}
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
            {failed && <Typography.Text type="danger">{t('web.recruitment.offer_response.failed')}</Typography.Text>}
            <Flex gap={8} wrap>
              <Popconfirm title={t('web.recruitment.offer_response.accept_confirm')} onConfirm={() => answer(true)}>
                <Button type="primary" loading={sending}>
                  {t('web.recruitment.offer_response.accept')}
                </Button>
              </Popconfirm>
              <Popconfirm title={t('web.recruitment.offer_response.decline_confirm')} onConfirm={() => answer(false)}>
                <Button danger loading={sending}>
                  {t('web.recruitment.offer_response.decline')}
                </Button>
              </Popconfirm>
            </Flex>
          </Flex>
        ) : (
          <Result
            status={offer.status === 'Accepted' ? 'success' : 'info'}
            title={t(`web.recruitment.offer_response.status.${offer.status}`)}
          />
        )}
      </Card>
    </div>
  );
};
