import { useQueryClient } from '@tanstack/react-query';
import { Col, Form, Input, InputNumber, Row, Select, Skeleton, Switch, TreeSelect } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import {
  getGetOrgUnitTreeQueryKey,
  getGetPositionQueryKey,
  getListJobsQueryKey,
  getListPositionsQueryKey,
  useCreatePosition,
  useGetOrgUnitTree,
  useGetPosition,
  useListJobs,
  useListPositions,
  useUpdatePosition,
} from '@/shared/api/generated/endpoints/hr/hr';
import { PositionStatus, type PositionDetails } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { buildUnitTree, toUnitOptions } from '../organization/unitTree';

interface PositionFormValues {
  companyId?: string;
  orgUnitId?: string;
  jobId?: string;
  code?: string;
  reportsToPositionId?: string | null;
  norm?: number;
  status?: PositionStatus;
  isManager?: boolean;
}

const LOOKUP_SIZE = 50;

const blank = (value?: string) => (value?.trim() ? value.trim() : null);

const toValues = (position: PositionDetails | undefined, companyId?: string, orgUnitId?: string): PositionFormValues =>
  position
    ? {
        companyId: position.companyId,
        orgUnitId: position.orgUnitId,
        jobId: position.jobId,
        code: position.code,
        reportsToPositionId: position.reportsToPositionId,
        norm: position.norm,
        status: position.status,
        isManager: position.isManager,
      }
    : { companyId, orgUnitId, norm: 1, isManager: false };

interface PositionFormDrawerProps {
  open: boolean;
  onClose: () => void;
  /** Position to edit; undefined creates one. */
  positionId?: string;
  /** Company of a new position; the active company when not given. */
  companyId?: string;
  /** Unit of a new position. */
  orgUnitId?: string;
}

/**
 * Create or edit a position (PLAN H1b): unit, job, the position it reports to, seats and status. The company and
 * the code are fixed after creation; job and manager lists are searched on the server.
 */
export const PositionFormDrawer = ({ open, onClose, positionId, companyId, orgUnitId }: PositionFormDrawerProps) => {
  const { t } = useTranslation();
  const me = useMe();
  const queryClient = useQueryClient();
  const editing = !!positionId;
  const position = useGetPosition(positionId ?? '', { query: { enabled: open && editing } });
  const create = useCreatePosition();
  const update = useUpdatePosition();
  const [form] = Form.useForm<PositionFormValues>();
  const selectedCompany = Form.useWatch('companyId', form) ?? position.data?.companyId ?? companyId;
  const [jobSearch, setJobSearch] = useState<string>();
  const [managerSearch, setManagerSearch] = useState<string>();
  const tree = useGetOrgUnitTree({ companyId: selectedCompany }, { query: { enabled: open && !!selectedCompany } });
  const jobs = useListJobs({ isActive: true, search: jobSearch, pageSize: LOOKUP_SIZE }, { query: { enabled: open } });
  const managers = useListPositions(
    { companyId: selectedCompany, search: managerSearch, pageSize: LOOKUP_SIZE },
    { query: { enabled: open && !!selectedCompany } },
  );
  const unitOptions = useMemo(() => toUnitOptions(buildUnitTree(tree.data?.units ?? [])), [tree.data]);

  const submit = async (values: PositionFormValues) => {
    if (editing && position.data) {
      await update.mutateAsync({
        id: position.data.id,
        data: {
          version: position.data.version,
          orgUnitId: values.orgUnitId ?? '',
          jobId: values.jobId ?? '',
          reportsToPositionId: values.reportsToPositionId ?? null,
          norm: values.norm ?? 1,
          status: values.status ?? null,
          isManager: values.isManager ?? false,
        },
      });
    } else {
      await create.mutateAsync({
        data: {
          orgUnitId: values.orgUnitId ?? '',
          jobId: values.jobId ?? '',
          code: blank(values.code),
          reportsToPositionId: values.reportsToPositionId ?? null,
          norm: values.norm ?? 1,
          isManager: values.isManager ?? false,
        },
      });
    }

    await Promise.all([
      queryClient.invalidateQueries({ queryKey: getListPositionsQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getGetOrgUnitTreeQueryKey() }),
      queryClient.invalidateQueries({ queryKey: getListJobsQueryKey() }),
      ...(positionId ? [queryClient.invalidateQueries({ queryKey: getGetPositionQueryKey(positionId) })] : []),
    ]);
  };

  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const current = position.data;
  // The current job and manager stay selectable even when the search does not return them
  const jobOptions = [
    ...(current && !jobs.data?.items.some((j) => j.id === current.jobId)
      ? [{ value: current.jobId, label: `${current.jobName} (${current.jobCode})` }]
      : []),
    ...(jobs.data?.items ?? []).map((j) => ({ value: j.id, label: `${j.name} (${j.code})` })),
  ];
  const managerOptions = [
    ...(current?.reportsToPositionId && !managers.data?.items.some((p) => p.id === current.reportsToPositionId)
      ? [{ value: current.reportsToPositionId, label: current.reportsToPositionCode ?? current.reportsToPositionId }]
      : []),
    ...(managers.data?.items ?? [])
      .filter((p) => p.id !== positionId && p.status !== PositionStatus.Closed)
      .map((p) => ({ value: p.id, label: `${p.code} · ${p.jobName} · ${p.orgUnitName}` })),
  ];
  const companies = current
    ? [{ value: current.companyId, label: current.companyName }]
    : me.companies.map((c) => ({ value: c.id, label: c.name }));

  return (
    <FormDrawer<PositionFormValues>
      key={current ? `${current.id}-${current.version}` : `new-${orgUnitId ?? ''}`}
      title={t(editing ? 'web.hr.positions.edit' : 'web.hr.positions.new')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      form={form}
      initialValues={toValues(current, companyId, orgUnitId)}
      width={600}
    >
      {editing && !current ? (
        <Skeleton active />
      ) : (
        <>
          <Form.Item name="companyId" label={t('web.hr.org.company')} rules={required}>
            <Select
              showSearch={{ optionFilterProp: 'label' }}
              disabled={editing}
              options={companies}
              onChange={() => form.setFieldsValue({ orgUnitId: undefined, reportsToPositionId: null })}
            />
          </Form.Item>
          <Form.Item name="orgUnitId" label={t('web.hr.positions.fields.org_unit')} rules={required}>
            <TreeSelect
              showSearch
              treeNodeFilterProp="title"
              treeDefaultExpandAll
              loading={tree.isLoading}
              treeData={unitOptions}
            />
          </Form.Item>
          <Row gutter={12}>
            <Col span={editing ? 24 : 14}>
              <Form.Item name="jobId" label={t('web.hr.positions.fields.job')} rules={required}>
                <Select
                  showSearch={{ filterOption: false, onSearch: (value) => setJobSearch(value.trim() || undefined) }}
                  loading={jobs.isFetching}
                  options={jobOptions}
                />
              </Form.Item>
            </Col>
            {!editing && (
              <Col span={10}>
                <Form.Item
                  name="code"
                  label={t('web.platform.fields.code')}
                  tooltip={t('web.platform.fields.code_hint')}
                  rules={required}
                >
                  <Input maxLength={20} style={{ textTransform: 'uppercase' }} />
                </Form.Item>
              </Col>
            )}
          </Row>
          <Form.Item
            name="reportsToPositionId"
            label={t('web.hr.positions.fields.reports_to')}
            extra={t('web.hr.positions.fields.reports_to_hint')}
          >
            <Select
              allowClear
              showSearch={{ filterOption: false, onSearch: (value) => setManagerSearch(value.trim() || undefined) }}
              loading={managers.isFetching}
              options={managerOptions}
            />
          </Form.Item>
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item name="norm" label={t('web.hr.positions.fields.norm')} rules={required}>
                <InputNumber min={1} max={999} precision={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            {editing && (
              <Col span={8}>
                <Form.Item name="status" label={t('web.hr.positions.fields.status')} rules={required}>
                  <Select
                    options={Object.values(PositionStatus).map((status) => ({
                      value: status,
                      label: t(`web.hr.position_status.${status}`),
                    }))}
                  />
                </Form.Item>
              </Col>
            )}
            <Col span={8}>
              <Form.Item name="isManager" label={t('web.hr.positions.fields.is_manager')} valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
          </Row>
        </>
      )}
    </FormDrawer>
  );
};
