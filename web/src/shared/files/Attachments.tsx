import { DeleteOutlined, DownloadOutlined, InboxOutlined, PaperClipOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Empty, Flex, List, Popconfirm, Skeleton, Typography, Upload } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { saveFile } from '@/shared/api/download';
import {
  deleteAttachment,
  downloadFile,
  getListAttachmentsQueryKey,
  listAttachments,
  uploadAttachment,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { AttachmentItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { formatDateTime } from '@/shared/format/date';
import { formatFileSize } from './fileSize';

/**
 * The generated API of each module that keeps attachments (D31). A module adds its functions here when its records
 * get attachments; the owner type's first segment picks the module.
 */
const moduleApis = {
  platform: {
    listKey: getListAttachmentsQueryKey,
    list: listAttachments,
    upload: uploadAttachment,
    remove: deleteAttachment,
    download: downloadFile,
  },
};

type AttachmentModule = keyof typeof moduleApis;

const apiOf = (ownerType: string) => {
  const module = ownerType.split('.')[0] as AttachmentModule;
  const api = moduleApis[module] as (typeof moduleApis)[AttachmentModule] | undefined;
  if (!api) {
    throw new Error(`No attachment API for ${ownerType}`);
  }

  return api;
};

interface AttachmentsProps {
  /** Record type, e.g. platform.company. */
  ownerType: string;
  ownerId: string;
}

/**
 * Files attached to a record (PLAN P7): drag and drop upload, list, download and delete. The server decides who may
 * see or change them from the record's permissions; without the manage right only the list is shown.
 */
export const Attachments = ({ ownerType, ownerId }: AttachmentsProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const api = apiOf(ownerType);
  const params = { ownerType, ownerId };
  const queryKey = api.listKey(params);
  const attachments = useQuery({ queryKey, queryFn: ({ signal }) => api.list(params, { signal }) });
  const uploadMutation = useMutation({ mutationFn: (body: Parameters<typeof api.upload>[0]) => api.upload(body) });
  const removeMutation = useMutation({ mutationFn: (id: string) => api.remove(id) });
  const [uploading, setUploading] = useState(0);

  const refresh = () => queryClient.invalidateQueries({ queryKey });

  /** Validation errors carry the reason (type, size, content); other errors are shown by the global notifier. */
  const showFileErrors = (error: unknown) => {
    const messages = isApiError(error) ? (error.problem?.fieldErrors ?? []).map((e) => e.message) : [];
    if (messages.length > 0) {
      void message.error(messages.join(' '));
    }
  };

  const upload = async (file: File) => {
    setUploading((count) => count + 1);
    try {
      await uploadMutation.mutateAsync({ ownerType, ownerId, file });
      void message.success(t('web.files.uploaded', { name: file.name }));
      await refresh();
    } catch (error) {
      showFileErrors(error);
    } finally {
      setUploading((count) => count - 1);
    }
  };

  const remove = async (item: AttachmentItem) => {
    await removeMutation.mutateAsync(item.id);
    void message.success(t('web.files.deleted'));
    await refresh();
  };

  if (!attachments.data) {
    return <Skeleton active paragraph={{ rows: 2 }} />;
  }

  const { items, canManage, extensions, maxBytes } = attachments.data;

  return (
    <Flex vertical gap={12}>
      {canManage && (
        <Upload.Dragger
          multiple
          showUploadList={false}
          accept={extensions.map((e) => `.${e}`).join(',')}
          disabled={uploading > 0}
          customRequest={({ file }) => void upload(file as File)}
        >
          <p className="ant-upload-drag-icon">
            <InboxOutlined />
          </p>
          <p className="ant-upload-text">{t('web.files.drop')}</p>
          <p className="ant-upload-hint">
            {t('web.files.hint', {
              extensions: extensions.join(', '),
              maxMegabytes: new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 }).format(
                maxBytes / (1024 * 1024),
              ),
            })}
          </p>
        </Upload.Dragger>
      )}
      {items.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.files.empty')} />
      ) : (
        <List
          size="small"
          dataSource={items}
          rowKey="id"
          renderItem={(item) => (
            <List.Item
              actions={[
                <Button
                  key="download"
                  type="text"
                  icon={<DownloadOutlined />}
                  aria-label={t('web.files.download')}
                  onClick={() => void saveFile(api.download(item.id), item.fileName).catch(() => undefined)}
                />,
                ...(canManage
                  ? [
                      <Popconfirm
                        key="delete"
                        title={t('web.files.delete_confirm', { name: item.fileName })}
                        okText={t('web.files.delete')}
                        cancelText={t('web.common.cancel')}
                        onConfirm={() => remove(item).catch(() => undefined)}
                      >
                        <Button type="text" danger icon={<DeleteOutlined />} aria-label={t('web.files.delete')} />
                      </Popconfirm>,
                    ]
                  : []),
              ]}
            >
              <List.Item.Meta
                avatar={<PaperClipOutlined />}
                title={<Typography.Text ellipsis={{ tooltip: item.fileName }}>{item.fileName}</Typography.Text>}
                description={[
                  formatFileSize(item.length, i18n.language),
                  formatDateTime(item.createdAt, i18n.language),
                  item.createdBy ? t('web.files.added_by', { name: item.createdBy }) : undefined,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              />
            </List.Item>
          )}
        />
      )}
    </Flex>
  );
};
