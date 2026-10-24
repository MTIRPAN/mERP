import { DownloadOutlined, InboxOutlined, UploadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Drawer,
  Flex,
  Popconfirm,
  Radio,
  Result,
  Spin,
  Steps,
  Table,
  Typography,
  Upload,
} from 'antd';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getImportReport,
  getImportTemplate,
  useCommitImport,
  useGetImportJob,
  useStartImport,
} from '@/shared/api/generated/endpoints/platform/platform';
import { ImportMode, ImportStatus, type ImportJobDetails } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { hasImported, isImportRunning, toErrorRows, type ImportErrorRow } from './importStatus';

/** Accepted file: the filled template. */
const XLSX = '.xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

const POLL_MS = 2000;

interface ImportDrawerProps {
  /** The import, e.g. platform.branches. */
  importKey: string;
  /** Name of the records, e.g. "Şubeler": file names and the title. */
  name: string;
  open: boolean;
  onClose: () => void;
  /** Records were written: refresh the page's list. */
  onImported: () => void;
  /** Rows of existing records may update them (D40); the user picks the mode before uploading. */
  supportsUpdate?: boolean;
}

const ErrorTable = ({ job }: { job: ImportJobDetails }) => {
  const { t } = useTranslation();
  const rows = useMemo(() => toErrorRows(job), [job]);
  return (
    <Table<ImportErrorRow>
      size="small"
      rowKey="key"
      dataSource={rows}
      pagination={rows.length > 10 ? { pageSize: 10, showSizeChanger: false } : false}
      columns={[
        {
          key: 'row',
          dataIndex: 'row',
          title: t('web.imports.errors.row'),
          width: 72,
          render: (row?: number) => row ?? '—',
        },
        {
          key: 'column',
          dataIndex: 'column',
          title: t('web.imports.errors.column'),
          width: 160,
          render: (c?: string) => c ?? '—',
        },
        { key: 'message', dataIndex: 'message', title: t('web.imports.errors.message') },
      ]}
      footer={
        job.errorCount > job.errors.length
          ? () => t('web.imports.errors.more', { shown: job.errors.length, total: job.errorCount })
          : undefined
      }
    />
  );
};

/**
 * Excel import of any module (D35, D40): download the template, upload the filled file, see the errors with their row
 * and column, then commit a file without errors or only its rows without errors. Imports that support updates ask
 * whether known records are updated. Large files are processed in the background; the drawer polls the job.
 */
export const ImportDrawer = ({ importKey, name, open, onClose, onImported, supportsUpdate }: ImportDrawerProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [jobId, setJobId] = useState<string>();
  const [mode, setMode] = useState<ImportMode>(ImportMode.Create);
  const start = useStartImport();
  const commit = useCommitImport();
  const { data: job, refetch } = useGetImportJob(jobId ?? '', {
    query: {
      enabled: !!jobId,
      refetchInterval: (query) => (isImportRunning(query.state.data?.status) ? POLL_MS : false),
    },
  });

  // Tell the page once per job that records were written
  const notified = useRef<string>(undefined);
  useEffect(() => {
    if (job && hasImported(job.status) && notified.current !== job.id) {
      notified.current = job.id;
      onImported();
    }
  }, [job, onImported]);

  const close = () => {
    setJobId(undefined);
    onClose();
  };

  const upload = async (file: File) => {
    try {
      const created = await start.mutateAsync({
        key: importKey,
        data: supportsUpdate ? { file, mode } : { file },
      });
      setJobId(created.id);
    } catch (error) {
      // Field errors (not an xlsx file, too many rows) are not shown by ApiErrorNotifier
      const detail = isApiError(error) ? error.problem?.fieldErrors?.[0]?.message : undefined;
      if (detail) {
        void message.error(detail);
      }
    }
  };

  const confirm = async (skipInvalidRows = false) => {
    if (!job) {
      return;
    }

    try {
      await commit.mutateAsync(skipInvalidRows ? { id: job.id, params: { skipInvalidRows } } : { id: job.id });
    } finally {
      await refetch();
    }
  };

  const downloadReport = () =>
    job &&
    void saveFile(
      getImportReport(job.id),
      `${job.fileName.replace(/.xlsx$/i, '')}-${t('web.imports.report_suffix')}.xlsx`,
    ).catch(() => undefined);

  const running = isImportRunning(job?.status);
  const step = !job ? 0 : job.status === ImportStatus.Ready || job.status === ImportStatus.Invalid ? 1 : 2;

  const outcome = () => {
    if (!job) {
      return null;
    }

    switch (job.status) {
      case ImportStatus.Queued:
      case ImportStatus.Validating:
      case ImportStatus.Committing:
        return (
          <Flex vertical align="center" gap={12} style={{ padding: 32 }}>
            <Spin size="large" />
            <Typography.Text>
              {t(job.status === ImportStatus.Committing ? 'web.imports.committing' : 'web.imports.validating', {
                rows: job.totalRows,
              })}
            </Typography.Text>
            <Typography.Text type="secondary">{t('web.imports.background_hint')}</Typography.Text>
          </Flex>
        );
      case ImportStatus.Ready:
        return (
          <Alert
            type="success"
            showIcon
            title={t('web.imports.ready', { rows: job.totalRows })}
            description={
              job.mode === ImportMode.CreateOrUpdate
                ? t('web.imports.ready_counts', { created: job.totalRows - job.updateRows, updated: job.updateRows })
                : t('web.imports.ready_hint')
            }
            action={
              <Button type="primary" loading={commit.isPending} onClick={() => void confirm()}>
                {t('web.imports.commit')}
              </Button>
            }
          />
        );
      case ImportStatus.Invalid:
        return (
          <Flex vertical gap={12}>
            <Alert
              type="error"
              showIcon
              title={t('web.imports.invalid', { rows: job.errorRows, total: job.totalRows })}
              description={t(
                job.canCommitValidRows ? 'web.imports.invalid_valid_rows_hint' : 'web.imports.invalid_hint',
              )}
              action={
                <Flex vertical align="flex-end" gap={8}>
                  <Button icon={<DownloadOutlined />} onClick={downloadReport}>
                    {t('web.imports.report')}
                  </Button>
                  {job.canCommitValidRows && (
                    <Popconfirm
                      title={t('web.imports.commit_valid_confirm', {
                        rows: job.totalRows - job.errorRows,
                        skipped: job.errorRows,
                      })}
                      okText={t('web.imports.commit_valid')}
                      cancelText={t('web.common.cancel')}
                      onConfirm={() => confirm(true)}
                    >
                      <Button loading={commit.isPending}>
                        {t('web.imports.commit_valid_count', { rows: job.totalRows - job.errorRows })}
                      </Button>
                    </Popconfirm>
                  )}
                </Flex>
              }
            />
            <ErrorTable job={job} />
          </Flex>
        );
      case ImportStatus.Completed:
        return <Result status="success" title={t('web.imports.completed', { rows: job.committedRows })} />;
      case ImportStatus.PartiallyCompleted:
        return (
          <Flex vertical gap={12}>
            <Alert
              type="warning"
              showIcon
              title={t('web.imports.partial', { committed: job.committedRows, failed: job.errorRows })}
              description={t('web.imports.partial_hint')}
              action={
                <Button icon={<DownloadOutlined />} onClick={downloadReport}>
                  {t('web.imports.report')}
                </Button>
              }
            />
            <ErrorTable job={job} />
          </Flex>
        );
      case ImportStatus.Failed:
        return <Result status="error" title={t('web.imports.failed')} subTitle={job.errors[0]?.message} />;
    }
  };

  return (
    <Drawer
      title={t('web.imports.title', { name })}
      open={open}
      onClose={close}
      size={720}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          {job && !running && (
            <Button icon={<UploadOutlined />} onClick={() => setJobId(undefined)}>
              {t('web.imports.another')}
            </Button>
          )}
          <Button onClick={close}>{t('web.common.close')}</Button>
        </Flex>
      }
    >
      <Flex vertical gap={24}>
        <Steps
          size="small"
          current={step}
          items={[
            { title: t('web.imports.steps.upload') },
            { title: t('web.imports.steps.check') },
            { title: t('web.imports.steps.done') },
          ]}
        />
        {!job ? (
          <Flex vertical gap={16}>
            <Typography.Paragraph style={{ marginBottom: 0 }}>{t('web.imports.intro')}</Typography.Paragraph>
            {supportsUpdate && (
              <Radio.Group
                value={mode}
                onChange={(event) => setMode(event.target.value as ImportMode)}
                aria-label={t('web.imports.mode.label')}
              >
                <Flex vertical gap={8}>
                  <Radio value={ImportMode.Create}>{t('web.imports.mode.create')}</Radio>
                  <Radio value={ImportMode.CreateOrUpdate}>
                    {t('web.imports.mode.create_or_update')}
                    <Typography.Text type="secondary" style={{ display: 'block' }}>
                      {t('web.imports.mode.create_or_update_hint')}
                    </Typography.Text>
                  </Radio>
                </Flex>
              </Radio.Group>
            )}
            <div>
              <Button
                icon={<DownloadOutlined />}
                onClick={() => void saveFile(getImportTemplate(importKey), `${name}.xlsx`).catch(() => undefined)}
              >
                {t('web.imports.template')}
              </Button>
            </div>
            <Spin spinning={start.isPending || (!!jobId && !job)}>
              <Upload.Dragger
                accept={XLSX}
                showUploadList={false}
                beforeUpload={(file) => {
                  void upload(file);
                  return false;
                }}
              >
                <p className="ant-upload-drag-icon">
                  <InboxOutlined />
                </p>
                <p className="ant-upload-text">{t('web.imports.drop')}</p>
                <p className="ant-upload-hint">{t('web.imports.drop_hint')}</p>
              </Upload.Dragger>
            </Spin>
          </Flex>
        ) : (
          <Flex vertical gap={12}>
            <Typography.Text type="secondary">{job.fileName}</Typography.Text>
            {outcome()}
          </Flex>
        )}
      </Flex>
    </Drawer>
  );
};
