import { DownloadOutlined, EditOutlined, UploadOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Flex, Form, Input, Modal, Select, Space, Switch, Tag, Typography, Upload } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { reloadTexts } from '@/app/i18n/i18n';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  exportTexts,
  getListCulturesQueryKey,
  getListTextsQueryKey,
  listTexts,
  useImportTexts,
  useSetText,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { CultureItem, TextItem } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { isApiError } from '@/shared/api/problem';
import type { ListParams } from '@/shared/api/paging';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { DataTable, type DataTableColumn } from '@/shared/components/DataTable';

/** Accepted import file: the xlsx of the export. */
const XLSX = '.xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

const TextEditor = ({
  culture,
  item,
  onClose,
  onSaved,
}: {
  culture: string;
  item?: TextItem;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) => {
  const { t } = useTranslation();
  const [form] = Form.useForm<{ value?: string }>();
  const setText = useSetText();

  const save = async ({ value }: { value?: string }) => {
    try {
      await setText.mutateAsync({ culture, data: { key: item!.key, value: value?.trim() ? value : null } });
      await onSaved();
      onClose();
    } catch (error) {
      applyApiErrorsToForm(form, error, { key: ['value'] });
    }
  };

  return (
    <Modal
      open={!!item}
      title={t('web.localization.texts.edit')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('web.common.save')}
      cancelText={t('web.common.cancel')}
      confirmLoading={setText.isPending}
      destroyOnHidden
    >
      {item && (
        <Form
          form={form}
          layout="vertical"
          initialValues={{ value: item.source === 'Database' ? item.text : undefined }}
          onFinish={save}
        >
          <Typography.Paragraph
            type="secondary"
            copyable={{ text: item.key }}
            style={{ fontFamily: 'monospace', fontSize: 12 }}
          >
            {item.key}
          </Typography.Paragraph>
          <Form.Item label={t('web.localization.texts.default_text')}>
            <Typography.Text>{item.defaultText ?? '—'}</Typography.Text>
          </Form.Item>
          {item.source === 'Embedded' && (
            <Form.Item label={t('web.localization.texts.shipped_text')}>
              <Typography.Text>{item.text}</Typography.Text>
            </Form.Item>
          )}
          <Form.Item
            name="value"
            label={t('web.localization.texts.translation')}
            extra={t('web.localization.texts.empty_hint')}
          >
            <Input.TextArea autoSize={{ minRows: 2, maxRows: 8 }} maxLength={4000} />
          </Form.Item>
        </Form>
      )}
    </Modal>
  );
};

/** The text catalog of one culture: search, missing ones, single edits, Excel export and import (D11). */
export const TextsCard = ({ cultures }: { cultures: CultureItem[] }) => {
  const { t, i18n } = useTranslation();
  const { message, modal } = App.useApp();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.localizationManage);
  const [culture, setCulture] = useState(() => (cultures.find((c) => !c.isDefault) ?? cultures[0])?.code ?? '');
  const [search, setSearch] = useState<string>();
  const [missing, setMissing] = useState(false);
  const [editing, setEditing] = useState<TextItem>();
  const importTexts = useImportTexts();

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: getListTextsQueryKey(culture) });
    await queryClient.invalidateQueries({ queryKey: getListCulturesQueryKey() });
    // The admin sees their own translations at once
    if (culture === i18n.language) {
      await reloadTexts();
    }
  };

  const columns = useMemo<DataTableColumn<TextItem>[]>(
    () => [
      {
        key: 'key',
        title: t('web.localization.texts.key'),
        width: 280,
        render: (_, row) => (
          <Typography.Text style={{ fontFamily: 'monospace', fontSize: 12 }} copyable={{ text: row.key }}>
            {row.key}
          </Typography.Text>
        ),
      },
      { key: 'defaultText', dataIndex: 'defaultText', title: t('web.localization.texts.default_text') },
      {
        key: 'text',
        title: t('web.localization.texts.translation'),
        render: (_, row) =>
          row.text === null ? (
            <Tag color="error" variant="filled">
              {t('web.localization.texts.missing')}
            </Tag>
          ) : (
            <Space size={6} align="start">
              <span>{row.text}</span>
              {row.source === 'Database' && (
                <Tag color="processing" variant="filled">
                  {t('web.localization.texts.custom')}
                </Tag>
              )}
            </Space>
          ),
      },
      ...(canManage
        ? [
            {
              key: 'actions',
              width: 56,
              render: (_: unknown, row: TextItem) => (
                <Button
                  type="text"
                  icon={<EditOutlined />}
                  aria-label={t('web.common.edit')}
                  onClick={() => setEditing(row)}
                />
              ),
            },
          ]
        : []),
    ],
    [t, canManage],
  );

  const fetchPage = (params: ListParams) =>
    listTexts(culture, {
      page: params.page,
      pageSize: params.pageSize,
      search: params.filter?.search,
      missing: params.filter?.missing === 'true' || undefined,
    });

  const upload = async (file: File) => {
    try {
      const result = await importTexts.mutateAsync({ culture, data: { file } });
      await refresh();
      modal.success({
        title: t('web.localization.import.done_title'),
        content: (
          <Flex vertical gap={8}>
            <span>
              {t('web.localization.import.done', {
                imported: result.imported,
                unchanged: result.unchanged,
                skipped: result.skipped,
              })}
            </span>
            {result.unknownKeys.length > 0 && (
              <Typography.Text type="warning">
                {t('web.localization.import.unknown_keys', { keys: result.unknownKeys.join(', ') })}
              </Typography.Text>
            )}
          </Flex>
        ),
      });
    } catch (error) {
      // Field errors (wrong file, other culture) are not shown by ApiErrorNotifier
      const detail = isApiError(error) ? error.problem?.fieldErrors?.[0]?.message : undefined;
      if (detail) {
        void message.error(detail);
      }
    }
  };

  return (
    <Card title={t('web.localization.texts.title')}>
      <DataTable<TextItem>
        queryKey={getListTextsQueryKey(culture)}
        fetchPage={fetchPage}
        columns={columns}
        rowKey="key"
        filter={{ search, missing: missing ? 'true' : undefined }}
        defaultPageSize={50}
        toolbar={
          <Space wrap>
            <Select
              style={{ minWidth: 200 }}
              value={culture}
              onChange={setCulture}
              aria-label={t('web.localization.texts.culture')}
              options={cultures.map((c) => ({ value: c.code, label: `${c.name} (${c.code})` }))}
            />
            <Input.Search
              allowClear
              placeholder={t('web.localization.texts.search')}
              onSearch={(value) => setSearch(value.trim() || undefined)}
              style={{ width: 260 }}
            />
            <Space size={6}>
              <Switch checked={missing} onChange={setMissing} aria-label={t('web.localization.texts.only_missing')} />
              {t('web.localization.texts.only_missing')}
            </Space>
            <Button
              icon={<DownloadOutlined />}
              onClick={() => void saveFile(exportTexts(culture), `translations-${culture}.xlsx`).catch(() => undefined)}
            >
              {t('web.localization.export')}
            </Button>
            {canManage && (
              <Upload
                accept={XLSX}
                showUploadList={false}
                beforeUpload={(file) => {
                  void upload(file);
                  return false;
                }}
              >
                <Button icon={<UploadOutlined />} loading={importTexts.isPending}>
                  {t('web.localization.import.button')}
                </Button>
              </Upload>
            )}
          </Space>
        }
      />
      <TextEditor culture={culture} item={editing} onClose={() => setEditing(undefined)} onSaved={refresh} />
    </Card>
  );
};
