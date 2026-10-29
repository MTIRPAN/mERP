import { Card, Empty, Flex, Space, Statistic, Tag, Typography, theme } from 'antd';
import { BarChart } from 'echarts/charts';
import { GridComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { SVGRenderer } from 'echarts/renderers';
import ReactEChartsCore from 'echarts-for-react/lib/core';
import { useTranslation } from 'react-i18next';
import type { CandidateEvaluation } from '@/shared/api/generated/model';
import { formatScore, recommendationColors } from './interviews';

echarts.use([BarChart, GridComponent, TooltipComponent, SVGRenderer]);

const BAR_HEIGHT = 32;

/** The average score of each criterion, from 1 to 5. */
const CriteriaChart = ({ evaluation }: { evaluation: CandidateEvaluation }) => {
  const { i18n } = useTranslation();
  const { token } = theme.useToken();
  // The first criterion on top
  const criteria = [...evaluation.criteria].reverse();

  return (
    <ReactEChartsCore
      echarts={echarts}
      opts={{ renderer: 'svg' }}
      style={{ height: Math.max(criteria.length * BAR_HEIGHT + 40, 120) }}
      option={{
        grid: { left: 8, right: 40, top: 8, bottom: 24, containLabel: true },
        tooltip: { trigger: 'item', valueFormatter: (value: number) => formatScore(value, i18n.language) },
        xAxis: {
          type: 'value',
          min: 0,
          max: 5,
          interval: 1,
          axisLabel: { color: token.colorTextSecondary },
          splitLine: { lineStyle: { color: token.colorBorderSecondary } },
        },
        yAxis: {
          type: 'category',
          data: criteria.map((c) => c.criterion),
          axisLabel: { color: token.colorText, width: 180, overflow: 'truncate' },
          axisLine: { lineStyle: { color: token.colorBorder } },
          axisTick: { show: false },
        },
        series: [
          {
            type: 'bar',
            data: criteria.map((c) => c.average),
            barMaxWidth: 18,
            itemStyle: { color: token.colorPrimary, borderRadius: [0, 4, 4, 0] },
            label: {
              show: true,
              position: 'right',
              color: token.colorText,
              formatter: ({ value }: { value: number }) => formatScore(value, i18n.language),
            },
          },
        ],
      }}
    />
  );
};

/**
 * The interviewers' evaluations of a candidate by application (PLAN R5, D52): the weighted score, the recommendations
 * and the average of each criterion.
 */
export const CandidateEvaluations = ({ evaluations }: { evaluations: CandidateEvaluation[] }) => {
  const { t, i18n } = useTranslation();

  if (evaluations.length === 0) {
    return <Empty description={t('web.recruitment.interviews.evaluations.none')} />;
  }

  return (
    <Flex vertical gap={16}>
      {evaluations.map((evaluation) => (
        <Card key={evaluation.applicationId} size="small" title={evaluation.openingTitle}>
          <Flex gap={32} wrap align="start" style={{ marginBottom: 12 }}>
            <Statistic
              title={t('web.recruitment.interviews.evaluations.score')}
              value={evaluation.score != null ? formatScore(evaluation.score, i18n.language) : '—'}
              suffix={evaluation.score != null ? '/ 5' : undefined}
            />
            <Statistic title={t('web.recruitment.interviews.evaluations.forms')} value={evaluation.feedbackCount} />
            <Flex vertical gap={4}>
              <Typography.Text type="secondary">
                {t('web.recruitment.interviews.feedback.recommendation')}
              </Typography.Text>
              <Space wrap>
                {evaluation.recommendations.map((r) => (
                  <Tag key={r.recommendation} color={recommendationColors[r.recommendation]}>
                    {t(`web.recruitment.interviews.recommendations.${r.recommendation}`)}: {r.count}
                  </Tag>
                ))}
              </Space>
            </Flex>
          </Flex>
          {evaluation.criteria.length > 0 && <CriteriaChart evaluation={evaluation} />}
        </Card>
      ))}
    </Flex>
  );
};
