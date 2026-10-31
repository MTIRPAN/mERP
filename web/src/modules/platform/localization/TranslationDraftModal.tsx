import { Alert, App, Checkbox, Flex, Input, Modal, Skeleton, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { draftTranslations, setTexts } from '@/shared/api/generated/endpoints/platform/platform';
import type { TranslationDraftResponse } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.localization.drafts';

/** The most texts one draft asks for (DraftTranslationsQuery.MaxCount). */
export const DRAFT_COUNT = 100;

/** The {name} placeholders of a text, sorted, to compare a translation with its source. */
const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

const samePlaceholders = (source: string, text: string) =>
  placeholders(source).join(',') === placeholders(text).join(',');

interface Row {
  key: string;
  source: string;
  text: string;
  include: boolean;
}

interface TranslationDraftModalProps {
  culture: string;
  cultureName: string;
  /** The list's search term: the drafts cover the missing texts it shows. */
  search?: string;
  onClose: () => void;
  onSaved: () => Promise<unknown>;
}

/**
 * Drafts the missing translations of the list's filter with AI and lets the user review them (PLAN A7, D60). Nothing
 * reaches the users until the reviewed rows are saved.
 */
export const TranslationDraftModal = ({
  culture,
  cultureName,
  search,
  onClose,
  onSaved,
}: TranslationDraftModalProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [response, setResponse] = useState<TranslationDraftResponse>();
  const [rows, setRows] = useState<Row[]>([]);
  const [failed, setFailed] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let current = true;
    draftTranslations(culture, { prefix: null, search: search ?? null, count: DRAFT_COUNT })
      .then((result) => {
        if (current) {
          setResponse(result);
          setRows(result.drafts.map((d) => ({ key: d.key, source: d.source, text: d.text, include: true })));
        }
      })
      .catch((error: unknown) => {
        if (!isApiError(error)) {
          throw error;
        }

        if (current) {
          setFailed(true);
        }
      });
    return () => {
      current = false;
    };
  }, [culture, search]);

  const update = (key: string, change: Partial<Row>) =>
    setRows((all) => all.map((r) => (r.key === key ? { ...r, ...change } : r)));
  const chosen = rows.filter((r) => r.include && r.text.trim());

  const save = async () => {
    setSaving(true);
    try {
      const result = await setTexts(culture, { texts: chosen.map((r) => ({ key: r.key, value: r.text.trim() })) });
      void message.success(t(`${prefix}.saved`, { count: result.changed }));
      await onSaved();
      onClose();
    } catch (error) {
      if (!isApiError(error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  const columns: TableColumnsType<Row> = [
    {
      key: 'include',
      title: t(`${prefix}.include`),
      width: 72,
      render: (_, row) => (
        <Checkbox
          checked={row.include}
          aria-label={`${t(`${prefix}.include`)} ${row.key}`}
          onChange={(e) => update(row.key, { include: e.target.checked })}
        />
      ),
    },
    {
      key: 'source',
      title: t(`${prefix}.source`),
      render: (_, row) => (
        <Flex vertical gap={2}>
          <Typography.Text type="secondary" style={{ fontFamily: 'monospace', fontSize: 12 }}>
            {row.key}
          </Typography.Text>
          <span>{row.source}</span>
        </Flex>
      ),
    },
    {
      key: 'text',
      title: t(`${prefix}.draft`),
      render: (_, row) => (
        <Flex vertical gap={4}>
          <Input.TextArea
            aria-label={row.key}
            value={row.text}
            maxLength={4000}
            autoSize={{ minRows: 1, maxRows: 6 }}
            onChange={(e) => update(row.key, { text: e.target.value })}
          />
          {!samePlaceholders(row.source, row.text) && <Tag color="warning">{t(`${prefix}.placeholders_differ`)}</Tag>}
        </Flex>
      ),
    },
  ];

  return (
    <Modal
      open
      width={960}
      title={
        <Space>
          {t(`${prefix}.title`, { culture: cultureName })}
          <AiBadge />
        </Space>
      }
      okText={t(`${prefix}.save`)}
      okButtonProps={{ loading: saving, disabled: chosen.length === 0 }}
      onOk={() => void save()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap={12}>
        <Alert type="info" showIcon title={t(`${prefix}.notice`)} />
        {!response && !failed && (
          <>
            <Typography.Text type="secondary">{t(`${prefix}.scope`, { count: DRAFT_COUNT })}</Typography.Text>
            <Skeleton active />
          </>
        )}
        {response && rows.length === 0 && <Typography.Text type="secondary">{t(`${prefix}.none`)}</Typography.Text>}
        {rows.length > 0 && (
          <Table<Row>
            rowKey="key"
            size="small"
            columns={columns}
            dataSource={rows}
            pagination={false}
            scroll={{ y: 480 }}
          />
        )}
        {response && response.remaining > 0 && (
          <Typography.Text type="secondary">{t(`${prefix}.remaining`, { count: response.remaining })}</Typography.Text>
        )}
      </Flex>
    </Modal>
  );
};
