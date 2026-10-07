import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Card, Flex, Popconfirm, Skeleton, Table, Typography, type TableColumnsType } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getListNumberSequencesQueryKey,
  useDeleteNumberSequence,
  useListNumberSequences,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { NumberSequenceItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { NumberSequenceDrawer } from './NumberSequenceDrawer';

/** Number patterns of the document types (PLAN P5, D29). */
export const NumberingPage = () => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.numberingManage);
  const sequences = useListNumberSequences();
  const remove = useDeleteNumberSequence();
  const [drawer, setDrawer] = useState<{ open: boolean; sequence?: NumberSequenceItem }>({ open: false });

  if (!sequences.data) {
    return <Skeleton active />;
  }

  const defaults = sequences.data.filter((s) => s.companyId === null);

  const deleteSequence = async (sequence: NumberSequenceItem) => {
    try {
      await remove.mutateAsync({ id: sequence.id });
      await queryClient.invalidateQueries({ queryKey: getListNumberSequencesQueryKey() });
    } catch {
      // Reported by ApiErrorNotifier
    }
  };

  const columns: TableColumnsType<NumberSequenceItem> = [
    {
      key: 'documentType',
      title: t('web.numbering.document_type'),
      render: (_, row) => (
        <Flex vertical>
          <Typography.Text strong={row.companyId === null}>{row.label}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {row.documentType}
          </Typography.Text>
        </Flex>
      ),
    },
    {
      key: 'company',
      title: t('web.numbering.company'),
      render: (_, row) => row.companyName ?? t('web.numbering.all_companies'),
    },
    {
      key: 'pattern',
      title: t('web.numbering.pattern'),
      render: (_, row) => <Typography.Text code>{row.pattern}</Typography.Text>,
    },
    {
      key: 'reset',
      title: t('web.numbering.reset'),
      render: (_, row) => t(`web.numbering.reset_period.${row.resetPeriod}`),
    },
    { key: 'example', dataIndex: 'example', title: t('web.numbering.example') },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 96,
            render: (_: unknown, row: NumberSequenceItem) => (
              <Flex gap={4}>
                <Button
                  type="text"
                  icon={<EditOutlined />}
                  aria-label={t('web.common.edit')}
                  onClick={() => setDrawer({ open: true, sequence: row })}
                />
                {row.companyId !== null && (
                  <Popconfirm
                    title={t('web.numbering.delete_confirm', { company: row.companyName ?? '' })}
                    okText={t('web.common.yes')}
                    cancelText={t('web.common.no')}
                    onConfirm={() => deleteSequence(row)}
                  >
                    <Button type="text" danger icon={<DeleteOutlined />} aria-label={t('web.numbering.delete')} />
                  </Popconfirm>
                )}
              </Flex>
            ),
          },
        ]
      : []),
  ];

  return (
    <>
      <PageHeader
        title={t('web.numbering.title')}
        subtitle={t('web.numbering.subtitle')}
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
              {t('web.numbering.new_company')}
            </Button>
          )
        }
      />
      <Card>
        <Table<NumberSequenceItem>
          rowKey="id"
          columns={columns}
          dataSource={sequences.data}
          pagination={false}
          locale={{ emptyText: t('web.numbering.empty') }}
          scroll={{ x: 'max-content' }}
        />
      </Card>
      <NumberSequenceDrawer
        sequence={drawer.sequence}
        defaults={defaults}
        open={drawer.open}
        onClose={() => setDrawer({ open: false })}
      />
    </>
  );
};
