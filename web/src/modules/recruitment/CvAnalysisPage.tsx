import { InboxOutlined, SafetyCertificateOutlined, StarFilled } from '@ant-design/icons';
import { Button, Card, Col, Empty, Flex, Form, Input, Row, Skeleton, Typography, Upload, theme } from 'antd';
import type { UploadFile } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { brand } from '@/app/theme/tokens';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { useAnalyzeCv } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CvAnalysisResponse } from '@/shared/api/generated/model';
import { AiBadge } from '@/shared/components/AiBadge';
import { PageHeader } from '@/shared/components/PageHeader';
import { cvAnalysisLimits } from './cvAnalysisLimits';
import { CvAnalysisResultView } from './CvAnalysisResultView';

interface CvAnalysisFormValues {
  file?: UploadFile[];
  jobTitle?: string;
  jobRequirements?: string;
  candidateName?: string;
}

const blankToUndefined = (value?: string) => (value?.trim() ? value.trim() : undefined);

export const CvAnalysisPage = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const [form] = Form.useForm<CvAnalysisFormValues>();
  const [result, setResult] = useState<CvAnalysisResponse>();
  const analyze = useAnalyzeCv();

  const submit = async (values: CvAnalysisFormValues) => {
    const file = values.file?.[0]?.originFileObj;
    if (!file) {
      return;
    }

    try {
      setResult(
        await analyze.mutateAsync({
          data: {
            file,
            jobTitle: blankToUndefined(values.jobTitle),
            jobRequirements: blankToUndefined(values.jobRequirements),
            candidateName: blankToUndefined(values.candidateName),
          },
        }),
      );
    } catch (error) {
      applyApiErrorsToForm(form, error);
    }
  };

  return (
    <>
      <PageHeader
        title={
          <Flex align="center" gap={token.marginSM}>
            {t('web.recruitment.cv_analysis.title')}
            <AiBadge />
          </Flex>
        }
        subtitle={t('web.recruitment.cv_analysis.subtitle')}
      />
      <Row gutter={[token.marginLG, token.marginLG]}>
        <Col xs={24} xl={9}>
          <Card title={t('web.recruitment.cv_analysis.form.title')}>
            <Form<CvAnalysisFormValues> form={form} layout="vertical" onFinish={submit} requiredMark="optional">
              <Form.Item
                name="file"
                label={t('web.recruitment.cv_analysis.form.file')}
                valuePropName="fileList"
                getValueFromEvent={(event: { fileList: UploadFile[] }) => event.fileList.slice(-1)}
                rules={[{ required: true, message: t('web.recruitment.cv_analysis.form.file_required') }]}
              >
                <Upload.Dragger
                  accept={cvAnalysisLimits.accept}
                  maxCount={1}
                  beforeUpload={(file) => {
                    if (file.size > cvAnalysisLimits.maxFileBytes) {
                      form.setFields([
                        { name: 'file', errors: [t('web.recruitment.cv_analysis.form.file_too_large')] },
                      ]);
                      return Upload.LIST_IGNORE;
                    }

                    // Kept in the form and sent with the analysis request, never uploaded on its own.
                    return false;
                  }}
                >
                  <p className="ant-upload-drag-icon">
                    <InboxOutlined />
                  </p>
                  <p className="ant-upload-text">{t('web.recruitment.cv_analysis.form.file_drop')}</p>
                  <p className="ant-upload-hint">{t('web.recruitment.cv_analysis.form.file_hint')}</p>
                </Upload.Dragger>
              </Form.Item>
              <Form.Item name="jobTitle" label={t('web.recruitment.cv_analysis.form.job_title')}>
                <Input
                  maxLength={cvAnalysisLimits.maxJobTitleLength}
                  placeholder={t('web.recruitment.cv_analysis.form.job_title_placeholder')}
                />
              </Form.Item>
              <Form.Item
                name="jobRequirements"
                label={t('web.recruitment.cv_analysis.form.job_requirements')}
                extra={t('web.recruitment.cv_analysis.form.job_requirements_help')}
              >
                <Input.TextArea
                  rows={7}
                  maxLength={cvAnalysisLimits.maxJobRequirementsLength}
                  placeholder={t('web.recruitment.cv_analysis.form.job_requirements_placeholder')}
                />
              </Form.Item>
              <Form.Item
                name="candidateName"
                label={t('web.recruitment.cv_analysis.form.candidate_name')}
                extra={t('web.recruitment.cv_analysis.form.candidate_name_help')}
              >
                <Input maxLength={cvAnalysisLimits.maxCandidateNameLength} autoComplete="off" />
              </Form.Item>
              <Flex
                gap={token.marginSM}
                align="flex-start"
                style={{
                  padding: token.paddingSM,
                  borderRadius: token.borderRadius,
                  background: token.colorFillQuaternary,
                  marginBottom: token.marginLG,
                }}
              >
                <SafetyCertificateOutlined style={{ color: token.colorSuccess, marginTop: 3 }} />
                <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                  {t('web.recruitment.cv_analysis.form.privacy')}
                </Typography.Text>
              </Flex>
              <Button
                type="primary"
                htmlType="submit"
                block
                size="large"
                icon={<StarFilled />}
                loading={analyze.isPending}
                style={{ background: brand.gradient, border: 'none' }}
              >
                {analyze.isPending
                  ? t('web.recruitment.cv_analysis.form.analyzing')
                  : t('web.recruitment.cv_analysis.form.submit')}
              </Button>
            </Form>
          </Card>
        </Col>
        <Col xs={24} xl={15}>
          {analyze.isPending ? (
            <Card>
              <Typography.Text type="secondary">{t('web.recruitment.cv_analysis.form.analyzing_hint')}</Typography.Text>
              <Skeleton active paragraph={{ rows: 10 }} style={{ marginTop: token.margin }} />
            </Card>
          ) : result ? (
            <CvAnalysisResultView result={result} />
          ) : (
            <Card style={{ height: '100%' }}>
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={t('web.recruitment.cv_analysis.empty')}
                style={{ padding: token.paddingXL }}
              />
            </Card>
          )}
        </Col>
      </Row>
    </>
  );
};
