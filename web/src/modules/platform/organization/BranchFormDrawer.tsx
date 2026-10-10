import { useQueryClient } from '@tanstack/react-query';
import { Col, Divider, Form, Input, Row, Select, Skeleton, Switch } from 'antd';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import {
  getGetBranchQueryKey,
  getListBranchesQueryKey,
  useCreateBranch,
  useGetBranch,
  useListWorkSchedules,
  useUpdateBranch,
} from '@/shared/api/generated/endpoints/platform/platform';
import { BranchType, type BranchDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { fromAddressDto, toAddressInput, type AddressValues } from './address';
import { AddressFields } from './AddressFields';

interface BranchFormValues {
  companyId?: string;
  code?: string;
  name?: string;
  type?: BranchType;
  sgkWorkplaceNumber?: string;
  phone?: string;
  isActive?: boolean;
  address?: AddressValues;
  workScheduleId?: string | null;
}

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

const toValues = (branch: BranchDetails | undefined, defaultCompanyId?: string): BranchFormValues =>
  branch
    ? {
        companyId: branch.companyId,
        code: branch.code,
        name: branch.name,
        type: branch.type,
        sgkWorkplaceNumber: branch.sgkWorkplaceNumber ?? undefined,
        phone: branch.phone ?? undefined,
        isActive: branch.isActive,
        address: fromAddressDto(branch.address),
        workScheduleId: branch.workScheduleId,
      }
    : { companyId: defaultCompanyId, type: BranchType.Office, isActive: true };

interface BranchFormDrawerProps {
  /** Branch to edit; undefined creates a new one in the active company. */
  branchId?: string;
  open: boolean;
  onClose: () => void;
}

/** Create or edit a branch. The company and the code are fixed after creation. */
export const BranchFormDrawer = ({ branchId, open, onClose }: BranchFormDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const queryClient = useQueryClient();
  const editing = !!branchId;
  const branch = useGetBranch(branchId ?? '', { query: { enabled: open && editing } });
  const create = useCreateBranch();
  const update = useUpdateBranch();
  const [form] = Form.useForm<BranchFormValues>();
  const companyId = Form.useWatch('companyId', form) ?? branch.data?.companyId ?? activeCompanyId;
  const schedules = useListWorkSchedules({ companyId }, { query: { enabled: open && !!companyId } });

  const submit = async (values: BranchFormValues) => {
    const fields = {
      name: blank(values.name),
      type: values.type ?? null,
      address: toAddressInput(values.address),
      sgkWorkplaceNumber: blank(values.sgkWorkplaceNumber),
      phone: blank(values.phone),
      workScheduleId: values.workScheduleId ?? null,
    };

    if (editing && branch.data) {
      await update.mutateAsync({
        id: branch.data.id,
        data: { ...fields, version: branch.data.version, isActive: values.isActive ?? true },
      });
    } else {
      await create.mutateAsync({ data: { ...fields, companyId: values.companyId ?? '', code: blank(values.code) } });
    }

    await queryClient.invalidateQueries({ queryKey: getListBranchesQueryKey() });
    if (branchId) {
      await queryClient.invalidateQueries({ queryKey: getGetBranchQueryKey(branchId) });
    }
  };

  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const companies =
    editing && branch.data
      ? [{ value: branch.data.companyId, label: branch.data.companyName }]
      : me.companies.map((c) => ({ value: c.id, label: c.name }));

  return (
    <FormDrawer<BranchFormValues>
      key={branch.data ? `${branch.data.id}-${branch.data.version}` : 'new'}
      title={t(editing ? 'web.platform.branches.edit' : 'web.platform.branches.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      initialValues={toValues(branch.data, activeCompanyId)}
      width={640}
    >
      {editing && !branch.data ? (
        <Skeleton active />
      ) : (
        <>
          <Form.Item name="companyId" label={t('web.platform.branches.company')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              disabled={editing}
              options={companies}
              onChange={() => form.setFieldValue('workScheduleId', null)}
            />
          </Form.Item>
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
              <Form.Item name="name" label={t('web.platform.branches.name')} rules={required}>
                <Input maxLength={150} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="type" label={t('web.platform.branches.type')} rules={required}>
                <Select
                  options={Object.values(BranchType).map((type) => ({
                    value: type,
                    label: t(`web.platform.branch_type.${type}`),
                  }))}
                />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="phone" label={t('web.platform.fields.phone')}>
                <Input maxLength={20} inputMode="tel" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="sgkWorkplaceNumber" label={t('web.platform.fields.sgk_workplace_number')}>
            <Input maxLength={40} inputMode="numeric" />
          </Form.Item>
          <Form.Item
            name="workScheduleId"
            label={t('web.platform.branches.work_schedule')}
            extra={t('web.platform.branches.work_schedule_hint')}
          >
            <Select
              allowClear
              loading={schedules.isLoading}
              placeholder={t('web.platform.branches.work_schedule_default')}
              options={(schedules.data ?? [])
                .filter((s) => s.isActive || s.id === branch.data?.workScheduleId)
                .map((s) => ({ value: s.id, label: s.displayName }))}
            />
          </Form.Item>
          {editing && (
            <Form.Item name="isActive" label={t('web.platform.fields.active')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Divider titlePlacement="start">{t('web.platform.address.title')}</Divider>
          <AddressFields required />
        </>
      )}
    </FormDrawer>
  );
};
