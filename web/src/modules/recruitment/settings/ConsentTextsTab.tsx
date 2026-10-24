import { PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, Empty, Flex, Form, Input, List, Segmented, Select, Skeleton, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import {
  getGetCurrentConsentTextQueryKey,
  getListConsentTextsQueryKey,
  useGetCurrentConsentText,
  useListConsentTexts,
  usePublishConsentText,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { ConsentKind } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { formatDateTime } from '@/shared/format/date';

const kinds: ConsentKind[] = ['Disclosure', 'ExplicitConsent'];
const ALL = 'all';

/** Paragraphs separated by empty lines, as candidates read them. */
const Paragraphs = ({ text }: { text: string }) =>
  text.split(/\n\s*\n/).map((paragraph, index) => (
    <Typography.Paragraph key={index} style={{ whiteSpace: 'pre-line' }}>
      {paragraph}
    </Typography.Paragraph>
  ));

/**
 * The KVKK texts candidates see (PLAN R1, D47): by kind, company (or every company) and language. A change is a new
 * version; candidates' consents keep the version they saw. While nothing is published the built-in text applies.
 */
export const ConsentTextsTab = ({ canManage }: { canManage: boolean }) => {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const [kind, setKind] = useState<ConsentKind>('Disclosure');
  const [company, setCompany] = useState<string>(ALL);
  const [culture, setCulture] = useState(DEFAULT_CULTURE);
  const [publishing, setPublishing] = useState(false);
  const companyId = company === ALL ? undefined : company;
  const current = useGetCurrentConsentText({ kind, companyId, culture });
  const history = useListConsentTexts({ kind, companyId });
  const publish = usePublishConsentText();
  const versions = (history.data ?? []).filter((v) => v.culture === culture && (v.companyId ?? ALL) === company);

  const submit = async ({ body }: { body?: string }) => {
    await publish.mutateAsync({ data: { kind, companyId: companyId ?? null, culture, body: body ?? null } });
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getListConsentTextsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getGetCurrentConsentTextQueryKey() }),
    ]);
  };

  return (
    <Flex vertical gap={12}>
      <Flex justify="space-between" gap={8} wrap>
        <Flex gap={8} wrap>
          <Segmented<ConsentKind>
            value={kind}
            onChange={setKind}
            options={kinds.map((k) => ({ value: k, label: t(`web.recruitment.settings.consents.kinds.${k}`) }))}
          />
          <Select
            aria-label={t('web.recruitment.settings.consents.company')}
            value={company}
            onChange={setCompany}
            options={[
              { value: ALL, label: t('web.recruitment.settings.consents.all_companies') },
              ...me.companies.map((c) => ({ value: c.id, label: c.name })),
            ]}
            style={{ minWidth: 200 }}
          />
          <Select
            aria-label={t('web.recruitment.settings.consents.language')}
            value={culture}
            onChange={setCulture}
            options={me.cultures.map((c) => ({ value: c.code, label: c.name }))}
            style={{ minWidth: 140 }}
          />
        </Flex>
        {canManage && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setPublishing(true)} disabled={!current.data}>
            {t('web.recruitment.settings.consents.publish')}
          </Button>
        )}
      </Flex>
      <Card
        size="small"
        title={t('web.recruitment.settings.consents.current')}
        extra={
          current.data &&
          (current.data.revision === 0 ? (
            <Tag>{t('web.recruitment.settings.consents.built_in')}</Tag>
          ) : (
            <Tag color="blue">
              {t('web.recruitment.settings.consents.revision', { revision: current.data.revision })}
              {current.data.companyId ? '' : ` · ${t('web.recruitment.settings.consents.all_companies')}`}
            </Tag>
          ))
        }
      >
        {current.isLoading ? <Skeleton active /> : current.data ? <Paragraphs text={current.data.body} /> : <Empty />}
      </Card>
      {versions.length > 0 && (
        <Card size="small" title={t('web.recruitment.settings.consents.history')}>
          <List
            size="small"
            dataSource={versions}
            renderItem={(version) => (
              <List.Item>
                <Typography.Text>
                  {t('web.recruitment.settings.consents.revision', { revision: version.revision })}
                </Typography.Text>
                <Typography.Text type="secondary">
                  {version.publishedAt ? formatDateTime(version.publishedAt, i18n.language) : ''}
                </Typography.Text>
              </List.Item>
            )}
          />
        </Card>
      )}
      {publishing && current.data && (
        <FormDrawer<{ body?: string }>
          title={t('web.recruitment.settings.consents.publish')}
          open
          width={720}
          onClose={() => setPublishing(false)}
          onSubmit={submit}
          submitLabel={t('web.recruitment.settings.consents.publish')}
          initialValues={{ body: current.data.body }}
        >
          <Typography.Paragraph type="secondary">
            {t('web.recruitment.settings.consents.publish_hint')}
          </Typography.Paragraph>
          <Form.Item name="body" rules={[{ required: true, message: t('web.platform.fields.required') }]}>
            <Input.TextArea
              aria-label={t('web.recruitment.settings.consents.body')}
              autoSize={{ minRows: 12, maxRows: 30 }}
              maxLength={20000}
              showCount
            />
          </Form.Item>
        </FormDrawer>
      )}
    </Flex>
  );
};
