import { Alert, Button, Flex, Select, Space, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ImportColumnItem, ImportFileInspection } from '@/shared/api/generated/model';
import { AiBadge } from '@/shared/components/AiBadge';

const prefix = 'web.imports.mapping';

/** Column key → header index, as the user maps the file (D62). */
export type ColumnMapping = Record<string, number>;

interface ImportMappingProps {
  inspection: ImportFileInspection;
  mapping: ColumnMapping;
  /** Columns the AI filled in, marked until the user changes them. */
  suggested: string[];
  onChange: (key: string, index: number | undefined) => void;
  /** Asks the AI for the columns not mapped yet; absent when the feature is off. */
  onSuggest?: () => void;
  suggesting: boolean;
}

/**
 * Maps a file's own headers to the columns of an import (PLAN A9, D62): every column gets a header of the file, the
 * ones named like the template preselected and the AI's suggestions marked.
 */
export const ImportMapping = ({
  inspection,
  mapping,
  suggested,
  onChange,
  onSuggest,
  suggesting,
}: ImportMappingProps) => {
  const { t } = useTranslation();
  const used = new Set(Object.values(mapping));
  const options = inspection.headers.map((h) => ({
    value: h.index,
    label: h.text || `#${h.index + 1}`,
    title: h.hint,
  }));

  return (
    <Flex vertical gap={12}>
      <Typography.Paragraph style={{ margin: 0 }}>{t(`${prefix}.hint`)}</Typography.Paragraph>
      {onSuggest && (
        <Flex gap={8} align="center" wrap>
          <Button loading={suggesting} onClick={onSuggest}>
            <Space size={4}>
              {t(`${prefix}.suggest`)}
              <AiBadge />
            </Space>
          </Button>
          <Typography.Text type="secondary">{t(`${prefix}.notice`)}</Typography.Text>
        </Flex>
      )}
      <Table<ImportColumnItem>
        size="small"
        rowKey="key"
        pagination={false}
        dataSource={inspection.columns}
        columns={[
          {
            key: 'column',
            title: t(`${prefix}.column`),
            render: (_, column) => (
              <span>
                {column.header}
                {column.required && <Typography.Text type="danger"> *</Typography.Text>}
              </span>
            ),
          },
          {
            key: 'header',
            title: t(`${prefix}.header`),
            width: 320,
            render: (_, column) => (
              <Space size={6} wrap>
                <Select<number>
                  allowClear
                  style={{ minWidth: 200 }}
                  aria-label={column.header}
                  placeholder={t(`${prefix}.none`)}
                  value={mapping[column.key]}
                  options={options.map((o) => ({
                    ...o,
                    disabled: used.has(o.value) && mapping[column.key] !== o.value,
                  }))}
                  onChange={(value) => onChange(column.key, value)}
                />
                {suggested.includes(column.key) && <Tag color="purple">{t(`${prefix}.suggested`)}</Tag>}
              </Space>
            ),
          },
        ]}
      />
      {inspection.columns.some((c) => c.required && mapping[c.key] === undefined) && (
        <Alert type="warning" showIcon title={t(`${prefix}.required_missing`)} />
      )}
    </Flex>
  );
};
