import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Card,
  Flex,
  Popconfirm,
  Progress,
  Skeleton,
  Table,
  Tag,
  Typography,
  type TableColumnsType,
} from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { platformPermissions } from '@/modules/platform/permissions';
import {
  getGetMeQueryKey,
  getListCulturesQueryKey,
  useDeleteCulture,
  useListCultures,
} from '@/shared/api/generated/endpoints/platform/platform';
import type { CultureItem } from '@/shared/api/generated/model';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { PageHeader } from '@/shared/components/PageHeader';
import { StatusTag } from '@/shared/components/StatusTag';
import { CultureDrawer } from './CultureDrawer';
import { coverage } from './cultures';
import { TextsCard } from './TextsCard';

/** Languages of the tenant and their translations (PLAN P4, D11). */
export const LocalizationPage = () => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const canManage = useHasPermission(platformPermissions.localizationManage);
  const cultures = useListCultures();
  const remove = useDeleteCulture();
  const [drawer, setDrawer] = useState<{ open: boolean; culture?: CultureItem }>({ open: false });

  if (!cultures.data) {
    return <Skeleton active />;
  }

  const byCode = new Map(cultures.data.map((c) => [c.code, c]));

  const deleteCulture = async (culture: CultureItem) => {
    try {
      await remove.mutateAsync({ code: culture.code });
      await queryClient.invalidateQueries({ queryKey: getListCulturesQueryKey() });
      await queryClient.invalidateQueries({ queryKey: getGetMeQueryKey() });
    } catch {
      // Reported by ApiErrorNotifier
    }
  };

  const columns: TableColumnsType<CultureItem> = [
    {
      key: 'name',
      title: t('web.localization.cultures.name'),
      render: (_, row) => (
        <Flex gap={8} align="center">
          {row.name}
          {row.isDefault && (
            <Tag color="processing" variant="filled">
              {t('web.localization.cultures.default')}
            </Tag>
          )}
        </Flex>
      ),
    },
    { key: 'code', dataIndex: 'code', title: t('web.localization.cultures.code') },
    {
      key: 'fallback',
      title: t('web.localization.cultures.fallback'),
      render: (_, row) =>
        row.isDefault
          ? '—'
          : (byCode.get(row.fallbackCode ?? '')?.name ?? t('web.localization.cultures.fallback_default')),
    },
    {
      key: 'coverage',
      title: t('web.localization.cultures.coverage'),
      width: 220,
      render: (_, row) => (
        <Progress
          percent={coverage(row.translatedCount, row.totalCount)}
          size="small"
          format={() => `${row.translatedCount} / ${row.totalCount}`}
        />
      ),
    },
    {
      key: 'status',
      title: t('web.platform.fields.status'),
      render: (_, row) => <StatusTag status={row.isActive ? 'active' : 'inactive'} />,
    },
    ...(canManage
      ? [
          {
            key: 'actions',
            width: 96,
            render: (_: unknown, row: CultureItem) => (
              <Flex gap={4}>
                <Button
                  type="text"
                  icon={<EditOutlined />}
                  aria-label={t('web.common.edit')}
                  onClick={() => setDrawer({ open: true, culture: row })}
                />
                {!row.isDefault && (
                  <Popconfirm
                    title={t('web.localization.cultures.delete_confirm', { name: row.name })}
                    okText={t('web.common.yes')}
                    cancelText={t('web.common.no')}
                    onConfirm={() => deleteCulture(row)}
                  >
                    <Button
                      type="text"
                      danger
                      icon={<DeleteOutlined />}
                      aria-label={t('web.localization.cultures.delete')}
                    />
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
      <PageHeader title={t('web.localization.title')} subtitle={t('web.localization.subtitle')} />
      <Flex vertical gap={16}>
        <Card
          title={t('web.localization.cultures.title')}
          extra={
            canManage && (
              <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true })}>
                {t('web.localization.cultures.new')}
              </Button>
            )
          }
        >
          <Typography.Paragraph type="secondary">{t('web.localization.cultures.hint')}</Typography.Paragraph>
          <Table<CultureItem>
            rowKey="code"
            columns={columns}
            dataSource={cultures.data}
            pagination={false}
            scroll={{ x: 'max-content' }}
          />
        </Card>
        <TextsCard cultures={cultures.data} />
      </Flex>
      <CultureDrawer
        culture={drawer.culture}
        cultures={cultures.data}
        open={drawer.open}
        onClose={() => setDrawer({ open: false })}
      />
    </>
  );
};
