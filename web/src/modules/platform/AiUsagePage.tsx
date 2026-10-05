import { Alert, Card, Col, DatePicker, Flex, Progress, Row, Skeleton, Statistic, Table, theme } from 'antd';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useGetAiUsageSummary } from '@/shared/api/generated/endpoints/platform/platform';
import type { AiFeatureUsageResponse } from '@/shared/api/generated/model';
import { AiBadge } from '@/shared/components/AiBadge';
import { PageHeader } from '@/shared/components/PageHeader';

/** AI calls, tokens and estimated cost of the tenant per month (D24). */
export const AiUsagePage = () => {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const [month, setMonth] = useState<Dayjs>(() => dayjs());
  const summary = useGetAiUsageSummary({ month: month.format('YYYY-MM') });
  const usd = new Intl.NumberFormat(i18n.language, { style: 'currency', currency: 'USD', maximumFractionDigits: 2 });
  const tokens = new Intl.NumberFormat(i18n.language, { notation: 'compact' });
  const data = summary.data;
  const budget = data?.monthlyBudgetUsd ?? 0;
  const cost = data?.costUsd ?? 0;

  return (
    <>
      <PageHeader
        title={
          <Flex align="center" gap={token.marginSM}>
            {t('web.ai_usage.title')}
            <AiBadge />
          </Flex>
        }
        subtitle={t('web.ai_usage.subtitle')}
        extra={
          <DatePicker
            picker="month"
            allowClear={false}
            value={month}
            onChange={(value) => value && setMonth(value)}
            disabledDate={(value) => value.isAfter(dayjs(), 'month')}
          />
        }
      />
      {!data ? (
        <Card>
          <Skeleton active />
        </Card>
      ) : (
        <Flex vertical gap={token.marginLG}>
          {!data.enabled && (
            <Alert
              type="warning"
              showIcon
              title={data.providerConfigured ? t('web.ai_usage.disabled_for_tenant') : t('web.ai_usage.not_configured')}
            />
          )}
          <Row gutter={[token.margin, token.margin]}>
            <Col xs={24} md={8}>
              <Card>
                <Statistic title={t('web.ai_usage.cost')} value={usd.format(cost)} />
                {budget > 0 && (
                  <Progress
                    percent={Math.min(100, Math.round((cost / budget) * 100))}
                    status={cost >= budget ? 'exception' : 'normal'}
                    format={() => t('web.ai_usage.of_budget', { budget: usd.format(budget) })}
                  />
                )}
              </Card>
            </Col>
            <Col xs={24} md={8}>
              <Card>
                <Statistic title={t('web.ai_usage.requests')} value={data.requests} />
              </Card>
            </Col>
            <Col xs={24} md={8}>
              <Card>
                <Statistic title={t('web.ai_usage.model')} value={data.model ?? '—'} />
              </Card>
            </Col>
          </Row>
          <Card title={t('web.ai_usage.by_feature')}>
            <Table<AiFeatureUsageResponse>
              rowKey="feature"
              pagination={false}
              dataSource={data.features}
              locale={{ emptyText: t('web.ai_usage.empty') }}
              columns={[
                {
                  key: 'feature',
                  title: t('web.ai_usage.feature'),
                  render: (_, row) => t(`web.ai.features.${row.feature}`, { defaultValue: row.feature }),
                },
                { key: 'requests', dataIndex: 'requests', title: t('web.ai_usage.requests'), align: 'end' },
                { key: 'succeeded', dataIndex: 'succeeded', title: t('web.ai_usage.succeeded'), align: 'end' },
                {
                  key: 'tokens',
                  title: t('web.ai_usage.tokens'),
                  align: 'end',
                  render: (_, row) => `${tokens.format(row.inputTokens)} / ${tokens.format(row.outputTokens)}`,
                },
                {
                  key: 'cost',
                  title: t('web.ai_usage.cost'),
                  align: 'end',
                  render: (_, row) => usd.format(row.costUsd),
                },
              ]}
            />
          </Card>
        </Flex>
      )}
    </>
  );
};
