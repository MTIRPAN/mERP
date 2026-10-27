import { ArrowLeftOutlined } from '@ant-design/icons';
import { Card, Divider, Flex, Result, Skeleton, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { submitCareerApplication, useGetCareerOpening } from '@/shared/api/generated/endpoints/careers/careers';
import { RichTextView } from '@/shared/components/RichTextEditor';
import { ApplicationForm } from './ApplicationForm';
import { OpeningFacts } from './careerParts';

/** An opening on the career page with its application form (PLAN R3b, D49). */
export const CareerOpeningPage = () => {
  const { t, i18n } = useTranslation();
  const { tenant = '', id = '' } = useParams();
  const details = useGetCareerOpening(tenant, id, { culture: i18n.language }, { query: { meta: { silent: true } } });
  const [sent, setSent] = useState(false);
  const back = (
    <Link to={`/careers/${tenant}`}>
      <ArrowLeftOutlined /> {t('web.recruitment.careers.all_openings')}
    </Link>
  );

  if (details.isError) {
    return (
      <div className="merp-careers">
        <Result status="404" title={t('web.recruitment.careers.opening_not_found')} extra={back} />
      </div>
    );
  }

  if (!details.data) {
    return (
      <div className="merp-careers">
        <Skeleton active />
      </div>
    );
  }

  const { opening, description, qualifications } = details.data;
  return (
    <div className="merp-careers">
      {back}
      <Typography.Title level={2} style={{ marginTop: 16 }}>
        {opening.title}
      </Typography.Title>
      <OpeningFacts opening={opening} />
      <Divider />
      <RichTextView html={description} />
      {qualifications && (
        <>
          <Typography.Title level={4}>{t('web.recruitment.careers.qualifications')}</Typography.Title>
          <RichTextView html={qualifications} />
        </>
      )}
      <Card title={t('web.recruitment.careers.apply_title')} style={{ marginTop: 24 }}>
        {sent ? (
          <Result
            status="success"
            title={t('web.recruitment.careers.sent_title')}
            subTitle={t('web.recruitment.careers.sent_body')}
            extra={back}
          />
        ) : (
          <Flex vertical>
            <ApplicationForm
              details={details.data}
              anonymous
              culture={i18n.language}
              onSubmit={async (payload, cv) => {
                await submitCareerApplication(tenant, id, { application: JSON.stringify(payload), cv });
                setSent(true);
              }}
            />
          </Flex>
        )}
      </Card>
    </div>
  );
};
