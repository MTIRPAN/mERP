import {
  Alert,
  App,
  Button,
  Checkbox,
  Col,
  DatePicker,
  Drawer,
  Flex,
  Form,
  Input,
  Radio,
  Row,
  Select,
  Table,
  Tag,
  TreeSelect,
} from 'antd';
import type { Dayjs } from 'dayjs';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMe } from '@/app/me/meContext';
import {
  createBulkLeave,
  previewBulkLeave,
  useGetOrgUnitTree,
  useListLeaveTypes,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { BulkLeaveRow, PreviewBulkLeaveQuery } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { buildUnitTree, toUnitOptions } from '../../organization/unitTree';
import { EmploymentsPicker } from './PeoplePickers';
import { useLeaveRefresh } from './leaveRequests';

interface BulkValues {
  companyId?: string;
  target?: 'people' | 'unit';
  employmentIds?: string[];
  orgUnitId?: string;
  includeSubUnits?: boolean;
  leaveTypeId?: string;
  range?: [Dayjs, Dayjs];
  note?: string;
}

/**
 * A bulk leave (toplu izin, PLAN L4, D44): people or a unit, a type and the days. The preview shows each person's
 * days, the balance after and what stops them; saving records the leave approved for those without a problem.
 */
export const BulkLeaveDrawer = ({ onClose }: { onClose: () => void }) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const me = useMe();
  const refresh = useLeaveRefresh();
  const [form] = Form.useForm<BulkValues>();
  const companyId = Form.useWatch('companyId', form);
  const target = Form.useWatch('target', form) ?? 'people';
  const types = useListLeaveTypes({ companyId }, { query: { enabled: !!companyId } });
  const tree = useGetOrgUnitTree({ companyId }, { query: { enabled: !!companyId && target === 'unit' } });
  const unitOptions = useMemo(() => toUnitOptions(buildUnitTree(tree.data?.units ?? [])), [tree.data]);
  const [rows, setRows] = useState<BulkLeaveRow[]>();
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState<'preview' | 'save'>();
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const query = (values: BulkValues): PreviewBulkLeaveQuery => ({
    companyId: values.companyId ?? '',
    employmentIds: values.target === 'unit' ? null : (values.employmentIds ?? []),
    orgUnitId: values.target === 'unit' ? (values.orgUnitId ?? null) : null,
    includeSubUnits: values.target === 'unit' && !!values.includeSubUnits,
    leaveTypeId: values.leaveTypeId ?? null,
    startDate: values.range![0].format('YYYY-MM-DD'),
    endDate: values.range![1].format('YYYY-MM-DD'),
    note: values.note?.trim() || null,
  });

  const run = async (save: boolean) => {
    let values: BulkValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }

    setBusy(save ? 'save' : 'preview');
    try {
      const result = save ? await createBulkLeave(query(values)) : await previewBulkLeave(query(values));
      setRows(result);
      setSaved(save);
      if (save) {
        void message.success(
          t('web.hr.leave_requests.bulk.saved', { count: result.filter((r) => r.requestId).length }),
        );
        await refresh();
      }
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setBusy(undefined);
    }
  };

  const days = (value: number | null) => (value == null ? '—' : value.toLocaleString(i18n.language));
  return (
    <Drawer
      title={t('web.hr.leave_requests.bulk.title')}
      open
      onClose={onClose}
      size={880}
      destroyOnHidden
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose}>{t(saved ? 'web.common.close' : 'web.common.cancel')}</Button>
          <Button loading={busy === 'preview'} disabled={!!busy || saved} onClick={() => void run(false)}>
            {t('web.hr.leave_requests.bulk.preview')}
          </Button>
          <Button
            type="primary"
            loading={busy === 'save'}
            disabled={!!busy || saved || !rows?.some((r) => !r.problem)}
            onClick={() => void run(true)}
          >
            {t('web.hr.leave_requests.bulk.save')}
          </Button>
        </Flex>
      }
    >
      <Form<BulkValues>
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={{ target: 'people', companyId: me.companies[0]?.id }}
        onValuesChange={() => {
          setRows(undefined);
          setSaved(false);
        }}
      >
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item name="companyId" label={t('web.hr.leave_requests.fields.company')} rules={required}>
              <Select
                options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
                onChange={() =>
                  form.setFieldsValue({ employmentIds: [], orgUnitId: undefined, leaveTypeId: undefined })
                }
              />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="leaveTypeId" label={t('web.hr.leave_requests.fields.type')} rules={required}>
              <Select
                loading={types.isFetching}
                options={(types.data ?? [])
                  .filter((type) => type.isActive && type.category !== 'Arrangement' && type.category !== 'Travel')
                  .map((type) => ({ value: type.id, label: type.name }))}
              />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="target">
          <Radio.Group
            options={[
              { value: 'people', label: t('web.hr.leave_requests.bulk.people') },
              { value: 'unit', label: t('web.hr.leave_requests.bulk.unit') },
            ]}
          />
        </Form.Item>
        {target === 'unit' ? (
          <Flex gap={12} align="center" wrap>
            <Form.Item
              name="orgUnitId"
              label={t('web.hr.leave_requests.bulk.unit')}
              rules={required}
              style={{ flex: 1, minWidth: 280 }}
            >
              <TreeSelect
                showSearch
                treeNodeFilterProp="title"
                treeDefaultExpandAll
                loading={tree.isLoading}
                treeData={unitOptions}
              />
            </Form.Item>
            <Form.Item name="includeSubUnits" valuePropName="checked">
              <Checkbox>{t('web.hr.leave_requests.bulk.include_sub_units')}</Checkbox>
            </Form.Item>
          </Flex>
        ) : (
          <Form.Item name="employmentIds" label={t('web.hr.leave_requests.bulk.people')} rules={required}>
            <EmploymentsPicker companyId={companyId} />
          </Form.Item>
        )}
        <Row gutter={12}>
          <Col span={12}>
            <Form.Item name="range" label={t('web.hr.leave_requests.fields.period')} rules={required}>
              <DatePicker.RangePicker format="L" style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="note" label={t('web.hr.leave_requests.fields.note')}>
              <Input maxLength={500} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
      {rows && (
        <>
          {saved && (
            <Alert
              type="success"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('web.hr.leave_requests.bulk.saved', { count: rows.filter((r) => r.requestId).length })}
            />
          )}
          <Table<BulkLeaveRow>
            size="small"
            rowKey="employmentId"
            dataSource={rows}
            pagination={rows.length > 50 ? { pageSize: 50 } : false}
            columns={[
              {
                key: 'name',
                title: t('web.hr.leave_requests.fields.employee'),
                render: (_, row) => `${row.employeeName} · ${row.registryNumber}`,
              },
              {
                key: 'days',
                title: t('web.hr.leave_requests.fields.length'),
                align: 'right',
                render: (_, row) => days(row.days),
              },
              {
                key: 'after',
                title: t('web.hr.leave_requests.preview.available_after'),
                align: 'right',
                render: (_, row) => days(row.availableAfter),
              },
              {
                key: 'result',
                title: t('web.hr.leave_requests.bulk.result'),
                render: (_, row) =>
                  row.problem ? (
                    <Tag color="error">{row.problem.message}</Tag>
                  ) : row.requestId ? (
                    <Tag color="success">{t('web.hr.leave_requests.bulk.recorded')}</Tag>
                  ) : (
                    <Flex gap={4} wrap>
                      <Tag color="processing">{t('web.hr.leave_requests.bulk.ready')}</Tag>
                      {row.warnings.map((w) => (
                        <Tag key={w.code} color="warning">
                          {w.message}
                        </Tag>
                      ))}
                    </Flex>
                  ),
              },
            ]}
          />
        </>
      )}
    </Drawer>
  );
};
