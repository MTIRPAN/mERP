import { DeleteOutlined, DownloadOutlined, EditOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Button, Empty, Flex, Popconfirm, Skeleton, Table, Tag, Upload } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  downloadEmployeeDocument,
  getListEmployeeDocumentsQueryKey,
  useDeleteEmployeeDocument,
  useListEmployeeDocuments,
  useReplaceEmployeeDocumentFile,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { EmployeeDocumentItem } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { formatDate } from '@/shared/format/date';
import { DocumentFormDrawer } from './DocumentFormDrawer';

const dash = '—';

/** Days left until a document expires, coloured as HR is reminded: 30 and 7 days before (D38). */
const DaysLeft = ({ days }: { days: number }) => {
  const { t } = useTranslation();
  if (days < 0) {
    return <Tag color="error">{t('web.hr.documents.expired')}</Tag>;
  }

  return (
    <Tag color={days <= 7 ? 'error' : days <= 30 ? 'warning' : 'default'}>
      {t('web.hr.documents.days_left', { days })}
    </Tag>
  );
};

type Drawer = { open: false } | { open: true; document?: EmployeeDocumentItem };

/** The person's documents with upload, download, change and delete for those who manage them (PLAN H2c, D38). */
export const DocumentsTab = ({ employeeId }: { employeeId: string }) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const documents = useListEmployeeDocuments(employeeId);
  const remove = useDeleteEmployeeDocument();
  const replace = useReplaceEmployeeDocumentFile();
  const [drawer, setDrawer] = useState<Drawer>({ open: false });

  if (!documents.data) {
    return documents.isError ? <Empty /> : <Skeleton active />;
  }

  const list = documents.data;
  const refresh = () => queryClient.invalidateQueries({ queryKey: getListEmployeeDocumentsQueryKey(employeeId) });
  const date = (value: string | null | undefined) => (value ? formatDate(value, i18n.language) : dash);

  const download = (row: EmployeeDocumentItem) =>
    void saveFile(downloadEmployeeDocument(employeeId, row.id), row.fileName ?? row.typeName).catch(() => undefined);

  const replaceFile = async (row: EmployeeDocumentItem, file: File) => {
    try {
      await replace.mutateAsync({ employeeId, documentId: row.id, data: { file } });
      void message.success(t('web.hr.documents.file_replaced'));
      await refresh();
    } catch (error) {
      const messages = isApiError(error) ? (error.problem?.fieldErrors ?? []).map((e) => e.message) : [];
      if (messages.length > 0) {
        void message.error(messages.join(' '));
      }
    }
  };

  const deleteDocument = async (row: EmployeeDocumentItem) => {
    await remove.mutateAsync({ employeeId, documentId: row.id });
    await refresh();
  };

  return (
    <Flex vertical gap={12}>
      {list.canManage && (
        <Flex justify="flex-end">
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
            {t('web.hr.documents.add')}
          </Button>
        </Flex>
      )}
      <Table<EmployeeDocumentItem>
        size="small"
        rowKey="id"
        pagination={false}
        dataSource={list.items}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.hr.documents.empty')} /> }}
        columns={[
          { key: 'type', dataIndex: 'typeName', title: t('web.hr.documents.fields.type') },
          { key: 'number', title: t('web.hr.documents.fields.number'), render: (_, row) => row.number ?? dash },
          { key: 'issuedOn', title: t('web.hr.documents.fields.issued_on'), render: (_, row) => date(row.issuedOn) },
          {
            key: 'validUntil',
            title: t('web.hr.documents.fields.valid_until'),
            render: (_, row) => (
              <Flex gap={8} align="center">
                {date(row.validUntil)}
                {row.daysLeft != null && <DaysLeft days={row.daysLeft} />}
              </Flex>
            ),
          },
          {
            key: 'note',
            title: t('web.hr.documents.fields.note'),
            ellipsis: true,
            render: (_, row) => row.note ?? dash,
          },
          {
            key: 'actions',
            width: 160,
            render: (_, row) => (
              <Flex gap={4}>
                <Button
                  type="text"
                  icon={<DownloadOutlined />}
                  aria-label={t('web.hr.documents.download')}
                  title={row.fileName ?? undefined}
                  disabled={!row.fileName}
                  onClick={() => download(row)}
                />
                {list.canManage && (
                  <>
                    <Button
                      type="text"
                      icon={<EditOutlined />}
                      aria-label={t('web.common.edit')}
                      onClick={() => setDrawer({ open: true, document: row })}
                    />
                    <Upload
                      showUploadList={false}
                      accept={list.extensions.map((e) => `.${e}`).join(',')}
                      customRequest={({ file }) => void replaceFile(row, file as File)}
                    >
                      <Button
                        type="text"
                        icon={<UploadOutlined />}
                        aria-label={t('web.hr.documents.replace_file')}
                        loading={replace.isPending && replace.variables?.documentId === row.id}
                      />
                    </Upload>
                    <Popconfirm
                      title={t('web.hr.documents.delete_confirm', { type: row.typeName })}
                      okText={t('web.common.yes')}
                      cancelText={t('web.common.no')}
                      onConfirm={() => deleteDocument(row).catch(() => undefined)}
                    >
                      <Button type="text" danger icon={<DeleteOutlined />} aria-label={t('web.common.delete')} />
                    </Popconfirm>
                  </>
                )}
              </Flex>
            ),
          },
        ]}
      />
      {drawer.open && (
        <DocumentFormDrawer
          open
          employeeId={employeeId}
          document={drawer.document}
          extensions={list.extensions}
          maxBytes={list.maxBytes}
          onClose={() => setDrawer({ open: false })}
        />
      )}
    </Flex>
  );
};
