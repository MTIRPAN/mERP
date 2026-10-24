import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { Button, Checkbox, Flex, Form, Input, InputNumber, Segmented, Table, Tag } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  getListRecruitmentLookupsQueryKey,
  useCreateRecruitmentLookup,
  useListRecruitmentLookups,
  useUpdateRecruitmentLookup,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { LookupKind, RecruitmentLookupItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { LocalizedNameFields } from './LocalizedNameFields';

const kinds: LookupKind[] = ['RejectionReason', 'WithdrawalReason', 'CandidateSource', 'Tag'];

interface LookupValues {
  code?: string;
  name?: Record<string, string>;
  sortOrder?: number;
  isActive?: boolean;
}

const LookupDrawer = ({
  kind,
  lookup,
  onClose,
}: {
  kind: LookupKind;
  lookup?: RecruitmentLookupItem;
  onClose: () => void;
}) => {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const create = useCreateRecruitmentLookup();
  const update = useUpdateRecruitmentLookup();

  const submit = async (values: LookupValues) => {
    if (lookup) {
      await update.mutateAsync({
        id: lookup.id,
        data: {
          version: lookup.version,
          name: values.name ?? {},
          sortOrder: values.sortOrder ?? 0,
          isActive: values.isActive ?? true,
        },
      });
    } else {
      await create.mutateAsync({
        data: { kind, code: values.code ?? null, name: values.name ?? {}, sortOrder: values.sortOrder ?? 0 },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListRecruitmentLookupsQueryKey() });
  };

  return (
    <FormDrawer<LookupValues>
      title={lookup ? lookup.name : t(`web.recruitment.settings.lookups.new.${kind}`)}
      open
      onClose={onClose}
      onSubmit={submit}
      initialValues={
        lookup
          ? { code: lookup.code, name: lookup.names, sortOrder: lookup.sortOrder, isActive: lookup.isActive }
          : { sortOrder: 0 }
      }
    >
      <Form.Item
        name="code"
        label={t('web.recruitment.settings.code')}
        extra={t('web.recruitment.settings.code_hint')}
        rules={[{ required: true, message: t('web.platform.fields.required') }]}
      >
        <Input maxLength={30} disabled={!!lookup} />
      </Form.Item>
      <LocalizedNameFields />
      <Form.Item name="sortOrder" label={t('web.recruitment.settings.sort_order')}>
        <InputNumber min={0} max={9999} precision={0} />
      </Form.Item>
      {lookup && (
        <Form.Item name="isActive" valuePropName="checked">
          <Checkbox>{t('web.recruitment.settings.active')}</Checkbox>
        </Form.Item>
      )}
    </FormDrawer>
  );
};

/** Rejection and withdrawal reasons, candidate sources and tags (PLAN R1, D47). */
export const LookupsTab = ({ canManage }: { canManage: boolean }) => {
  const { t } = useTranslation();
  const [kind, setKind] = useState<LookupKind>('RejectionReason');
  const lookups = useListRecruitmentLookups({ kind, includeInactive: true });
  const [editing, setEditing] = useState<{ lookup?: RecruitmentLookupItem }>();

  return (
    <Flex vertical gap={12}>
      <Flex justify="space-between" gap={8} wrap>
        <Segmented<LookupKind>
          value={kind}
          onChange={setKind}
          options={kinds.map((k) => ({ value: k, label: t(`web.recruitment.settings.lookups.kinds.${k}`) }))}
        />
        {canManage && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing({})}>
            {t(`web.recruitment.settings.lookups.new.${kind}`)}
          </Button>
        )}
      </Flex>
      <Table<RecruitmentLookupItem>
        size="small"
        rowKey="id"
        loading={lookups.isLoading}
        dataSource={lookups.data}
        pagination={false}
        columns={[
          { key: 'code', dataIndex: 'code', title: t('web.recruitment.settings.code'), width: 200 },
          { key: 'name', dataIndex: 'name', title: t('web.recruitment.settings.name') },
          {
            key: 'sortOrder',
            dataIndex: 'sortOrder',
            title: t('web.recruitment.settings.sort_order'),
            width: 100,
            align: 'right',
          },
          {
            key: 'isActive',
            title: t('web.recruitment.settings.status'),
            width: 110,
            render: (_, row) =>
              row.isActive ? (
                <Tag color="green">{t('web.recruitment.settings.active')}</Tag>
              ) : (
                <Tag>{t('web.recruitment.settings.inactive')}</Tag>
              ),
          },
          ...(canManage
            ? [
                {
                  key: 'actions',
                  width: 60,
                  render: (_: unknown, row: RecruitmentLookupItem) => (
                    <Button
                      size="small"
                      icon={<EditOutlined />}
                      onClick={() => setEditing({ lookup: row })}
                      aria-label={t('web.common.edit')}
                    />
                  ),
                },
              ]
            : []),
        ]}
      />
      {editing && <LookupDrawer kind={kind} lookup={editing.lookup} onClose={() => setEditing(undefined)} />}
    </Flex>
  );
};
