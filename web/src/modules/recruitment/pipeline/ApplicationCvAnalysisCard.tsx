import { RobotOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Flex, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  analyzeApplicationCv,
  getGetApplicationQueryKey,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { ApplicationCvAnalysisItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { formatDateTime } from '@/shared/format/date';
import { CvAnalysisResultView } from '../CvAnalysisResultView';

interface ApplicationCvAnalysisCardProps {
  applicationId: string;
  analysis: ApplicationCvAnalysisItem | null;
  /** The caller may run it (again): the feature is on, the application has a CV and the permission is there. */
  canAnalyze: boolean;
}

/**
 * The AI analysis of the application's CV against the opening (PLAN A3, D51): a suggestion for the team, never a
 * decision. New applications are analyzed in the background; those allowed run it again here.
 */
export const ApplicationCvAnalysisCard = ({ applicationId, analysis, canAnalyze }: ApplicationCvAnalysisCardProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const [running, setRunning] = useState(false);

  const run = async () => {
    setRunning(true);
    try {
      await analyzeApplicationCv(applicationId);
      await queryClient.invalidateQueries({ queryKey: getGetApplicationQueryKey(applicationId) });
    } catch (error) {
      void message.error(
        isApiError(error) ? (error.problem?.fieldErrors?.[0]?.message ?? error.message) : String(error),
      );
    } finally {
      setRunning(false);
    }
  };

  const action = canAnalyze && (
    <Button size="small" icon={<RobotOutlined />} loading={running} onClick={() => void run()}>
      {t(analysis ? 'web.recruitment.pipeline.cv_analysis.again' : 'web.recruitment.pipeline.cv_analysis.run')}
    </Button>
  );

  return (
    <Card size="small" title={t('web.recruitment.pipeline.cv_analysis.title')} extra={action}>
      {!analysis ? (
        <Typography.Text type="secondary">
          {t(
            canAnalyze
              ? 'web.recruitment.pipeline.cv_analysis.none'
              : 'web.recruitment.pipeline.cv_analysis.none_background',
          )}
        </Typography.Text>
      ) : (
        <Flex vertical gap={12}>
          <Typography.Text type="secondary">
            {t('web.recruitment.pipeline.cv_analysis.when', {
              at: formatDateTime(analysis.analyzedAt, i18n.language),
              by: analysis.requestedByName ?? t('web.recruitment.pipeline.cv_analysis.background'),
            })}
          </Typography.Text>
          {analysis.succeeded && analysis.response ? (
            <CvAnalysisResultView result={analysis.response} />
          ) : (
            <Alert
              type="warning"
              showIcon
              title={t('web.recruitment.pipeline.cv_analysis.failed')}
              description={analysis.errorCode ? t(analysis.errorCode) : undefined}
            />
          )}
        </Flex>
      )}
    </Card>
  );
};
