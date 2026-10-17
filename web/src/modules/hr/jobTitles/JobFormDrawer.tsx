import { useQueryClient } from '@tanstack/react-query';
import { Col, Form, Input, Row, Skeleton, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  getGetJobQueryKey,
  getListJobsQueryKey,
  useCreateJob,
  useGetJob,
  useUpdateJob,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { JobDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';

interface JobFormValues {
  code?: string;
  name?: string;
  family?: string;
  level?: string;
  isActive?: boolean;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

const toValues = (job: JobDetails | undefined): JobFormValues =>
  job
    ? {
        code: job.code,
        name: job.name,
        family: job.family ?? undefined,
        level: job.level ?? undefined,
        isActive: job.isActive,
      }
    : { isActive: true };

interface JobFormDrawerProps {
  open: boolean;
  onClose: () => void;
  /** Job to edit; undefined creates one. */
  jobId?: string;
}

/** Create or edit a job of the shared catalog (D37); the code is fixed after creation. */
export const JobFormDrawer = ({ open, onClose, jobId }: JobFormDrawerProps) => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const editing = !!jobId;
  const job = useGetJob(jobId ?? '', { query: { enabled: open && editing } });
  const create = useCreateJob();
  const update = useUpdateJob();

  const submit = async (values: JobFormValues) => {
    const fields = { name: blank(values.name), family: blank(values.family), level: blank(values.level) };
    if (editing && job.data) {
      await update.mutateAsync({
        id: job.data.id,
        data: { ...fields, version: job.data.version, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({ data: { ...fields, code: blank(values.code) } });
    }

    await queryClient.invalidateQueries({ queryKey: getListJobsQueryKey() });
    if (jobId) {
      await queryClient.invalidateQueries({ queryKey: getGetJobQueryKey(jobId) });
    }
  };

  const required = [{ required: true, message: t('web.platform.fields.required') }];

  return (
    <FormDrawer<JobFormValues>
      key={job.data ? `${job.data.id}-${job.data.version}` : 'new'}
      title={t(editing ? 'web.hr.job_titles.edit' : 'web.hr.job_titles.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={toValues(job.data)}
    >
      {editing && !job.data ? (
        <Skeleton active />
      ) : (
        <>
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item
                name="code"
                label={t('web.platform.fields.code')}
                tooltip={t('web.platform.fields.code_hint')}
                rules={required}
              >
                <Input maxLength={20} disabled={editing} style={{ textTransform: 'uppercase' }} />
              </Form.Item>
            </Col>
            <Col span={16}>
              <Form.Item name="name" label={t('web.hr.job_titles.fields.name')} rules={required}>
                <Input maxLength={150} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={16}>
              <Form.Item name="family" label={t('web.hr.job_titles.fields.family')}>
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item
                name="level"
                label={t('web.hr.job_titles.fields.level')}
                tooltip={t('web.hr.job_titles.fields.level_hint')}
              >
                <Input maxLength={10} />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item name="isActive" label={t('web.platform.fields.active')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
        </>
      )}
    </FormDrawer>
  );
};
