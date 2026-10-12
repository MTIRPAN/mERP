import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Card,
  Col,
  Divider,
  Drawer,
  Flex,
  Form,
  Input,
  InputNumber,
  Radio,
  Row,
  Select,
  Skeleton,
  Switch,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { DEFAULT_CULTURE } from '@/app/i18n/i18n';
import { useMe } from '@/app/me/meContext';
import {
  getGetApprovalDefinitionQueryKey,
  getListApprovalDefinitionsQueryKey,
  useCreateApprovalDefinition,
  useGetApprovalDefinition,
  useListApproverResolvers,
  useUpdateApprovalDefinition,
} from '@/shared/api/generated/endpoints/platform/platform';
import {
  ApprovalEscalation,
  ApprovalMode,
  type ApprovalDefinitionDetails,
  type ApprovalDocumentTypeItem,
} from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { ApproverFields } from './ApproverFields';
import { ConditionBuilder } from './ConditionBuilder';
import { newStep, toCommand, toFormValues, type FlowValues } from './flowForm';

interface ApprovalFlowDrawerProps {
  documentType: ApprovalDocumentTypeItem;
  /** Definition to change; undefined defines a new flow. */
  definitionId?: string;
  open: boolean;
  onClose: () => void;
}

/**
 * The flow editor (§2.1–§2.5): name, company, priority, entry condition, steps with resolver, mode, condition, due
 * time and escalation, and the fallback approver. The document type and company are fixed after creation.
 */
export const ApprovalFlowDrawer = ({ documentType, definitionId, open, onClose }: ApprovalFlowDrawerProps) => {
  const definition = useGetApprovalDefinition(definitionId ?? '', { query: { enabled: open && !!definitionId } });

  if (definitionId && !definition.data) {
    return (
      <Drawer open={open} onClose={onClose} size={900}>
        <Skeleton active />
      </Drawer>
    );
  }

  return (
    <FlowForm
      key={definition.data ? `${definition.data.id}-${definition.data.version}` : 'new'}
      documentType={documentType}
      definition={definition.data}
      open={open}
      onClose={onClose}
    />
  );
};

const FlowForm = ({
  documentType,
  definition,
  open,
  onClose,
}: Omit<ApprovalFlowDrawerProps, 'definitionId'> & { definition?: ApprovalDefinitionDetails }) => {
  const { t } = useTranslation();
  const me = useMe();
  const [form] = Form.useForm<FlowValues>();
  const queryClient = useQueryClient();
  const resolvers = useListApproverResolvers();
  const create = useCreateApprovalDefinition();
  const update = useUpdateApprovalDefinition();
  const facts = documentType.facts;
  const required = [{ required: true, message: t('web.platform.fields.required') }];

  const submit = async (values: FlowValues) => {
    const command = toCommand(values, DEFAULT_CULTURE);
    if (definition) {
      await update.mutateAsync({
        id: definition.id,
        data: { ...command, version: definition.version, isActive: values.isActive ?? true },
      });
      await queryClient.invalidateQueries({ queryKey: getGetApprovalDefinitionQueryKey(definition.id) });
    } else {
      await create.mutateAsync({
        data: { ...command, documentType: documentType.key, companyId: values.companyId ?? null },
      });
    }

    await queryClient.invalidateQueries({ queryKey: [getListApprovalDefinitionsQueryKey()[0]] });
  };

  const initialValues: FlowValues = definition
    ? toFormValues(definition, DEFAULT_CULTURE)
    : { priority: 0, isActive: true, steps: [newStep()] };

  return (
    <FormDrawer<FlowValues>
      form={form}
      title={t(definition ? 'web.approvals.flows.edit' : 'web.approvals.flows.new', { type: documentType.name })}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      initialValues={initialValues}
      width={900}
      fieldAliases={{ name: ['name', DEFAULT_CULTURE] }}
    >
      <Row gutter={12}>
        {me.cultures.map((culture) => (
          <Col xs={24} md={12} key={culture.code}>
            <Form.Item
              name={['name', culture.code]}
              label={
                me.cultures.length > 1
                  ? t('web.approvals.flows.name_in', { language: culture.name })
                  : t('web.approvals.flows.name')
              }
              rules={culture.code === DEFAULT_CULTURE ? required : undefined}
            >
              <Input maxLength={200} />
            </Form.Item>
          </Col>
        ))}
      </Row>
      <Row gutter={12}>
        <Col xs={24} md={12}>
          <Form.Item
            name="companyId"
            label={t('web.approvals.flows.company')}
            tooltip={t('web.approvals.flows.company_hint')}
          >
            <Select
              allowClear
              disabled={!!definition}
              placeholder={t('web.approvals.flows.all_companies')}
              options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
            />
          </Form.Item>
        </Col>
        <Col xs={12} md={6}>
          <Form.Item
            name="priority"
            label={t('web.approvals.flows.priority')}
            tooltip={t('web.approvals.flows.priority_hint')}
          >
            <InputNumber style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        {!!definition && (
          <Col xs={12} md={6}>
            <Form.Item name="isActive" label={t('web.platform.fields.status')} valuePropName="checked">
              <Switch checkedChildren={t('web.status.active')} unCheckedChildren={t('web.status.inactive')} />
            </Form.Item>
          </Col>
        )}
      </Row>
      <Form.Item
        name="entryCondition"
        label={t('web.approvals.flows.entry_condition')}
        tooltip={t('web.approvals.flows.entry_condition_hint')}
      >
        <ConditionBuilder facts={facts} addLabel={t('web.approvals.condition.add')} />
      </Form.Item>
      <Divider titlePlacement="start">{t('web.approvals.flows.steps')}</Divider>
      <Form.List name="steps">
        {(steps, { add, remove, move }, { errors }) => (
          <Flex vertical gap={12}>
            {steps.map(({ key, name }, index) => (
              <Card
                key={key}
                size="small"
                title={t('web.approvals.flows.step_title', { index: index + 1 })}
                extra={
                  <Flex gap={4}>
                    <Button
                      type="text"
                      size="small"
                      icon={<ArrowUpOutlined />}
                      disabled={index === 0}
                      aria-label={t('web.approvals.flows.move_up')}
                      onClick={() => move(index, index - 1)}
                    />
                    <Button
                      type="text"
                      size="small"
                      icon={<ArrowDownOutlined />}
                      disabled={index === steps.length - 1}
                      aria-label={t('web.approvals.flows.move_down')}
                      onClick={() => move(index, index + 1)}
                    />
                    <Button
                      type="text"
                      size="small"
                      danger
                      icon={<DeleteOutlined />}
                      disabled={steps.length === 1}
                      aria-label={t('web.approvals.flows.remove_step')}
                      onClick={() => remove(name)}
                    />
                  </Flex>
                }
              >
                <Form.Item name={[name, 'names']} hidden noStyle />
                <Form.Item name={[name, 'name']} label={t('web.approvals.flows.step_name')} rules={required}>
                  <Input maxLength={200} />
                </Form.Item>
                <ApproverFields
                  name={[name, 'approver']}
                  path={['steps', name, 'approver']}
                  form={form}
                  resolvers={resolvers.data ?? []}
                  facts={facts}
                  label={t('web.approvals.flows.approver')}
                />
                <Row gutter={12}>
                  <Col xs={24} md={8}>
                    <Form.Item name={[name, 'mode']} label={t('web.approvals.flows.mode')}>
                      <Radio.Group
                        optionType="button"
                        options={Object.values(ApprovalMode).map((mode) => ({
                          value: mode,
                          label: t(`web.approvals.modes.${mode}`),
                        }))}
                      />
                    </Form.Item>
                  </Col>
                  <Col xs={12} md={6}>
                    <Form.Item
                      name={[name, 'dueHours']}
                      label={t('web.approvals.flows.due_hours')}
                      tooltip={t('web.approvals.flows.due_hours_hint')}
                    >
                      <InputNumber min={1} max={8760} style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>
                  <Col xs={12} md={10}>
                    <Form.Item name={[name, 'escalation']} label={t('web.approvals.flows.escalation')}>
                      <Select
                        options={Object.values(ApprovalEscalation).map((value) => ({
                          value,
                          label: t(`web.approvals.escalations.${value}`),
                        }))}
                      />
                    </Form.Item>
                  </Col>
                </Row>
                <Form.Item
                  name={[name, 'condition']}
                  label={t('web.approvals.flows.step_condition')}
                  tooltip={t('web.approvals.flows.step_condition_hint')}
                >
                  <ConditionBuilder facts={facts} addLabel={t('web.approvals.condition.add')} />
                </Form.Item>
                <Flex gap={24} wrap>
                  <Form.Item
                    name={[name, 'skipIfApproverIsRequester']}
                    valuePropName="checked"
                    style={{ marginBottom: 0 }}
                  >
                    <Switch size="small" />
                  </Form.Item>
                  <Typography.Text>{t('web.approvals.flows.skip_requester')}</Typography.Text>
                  <Form.Item name={[name, 'skipIfAlreadyApproved']} valuePropName="checked" style={{ marginBottom: 0 }}>
                    <Switch size="small" />
                  </Form.Item>
                  <Typography.Text>{t('web.approvals.flows.skip_already_approved')}</Typography.Text>
                  <Form.Item name={[name, 'allowReturn']} valuePropName="checked" style={{ marginBottom: 0 }}>
                    <Switch size="small" />
                  </Form.Item>
                  <Typography.Text>{t('web.approvals.flows.allow_return')}</Typography.Text>
                </Flex>
              </Card>
            ))}
            <Form.ErrorList errors={errors} />
            <Button type="dashed" icon={<PlusOutlined />} disabled={steps.length >= 20} onClick={() => add(newStep())}>
              {t('web.approvals.flows.add_step')}
            </Button>
          </Flex>
        )}
      </Form.List>
      <Divider titlePlacement="start">{t('web.approvals.flows.fallback')}</Divider>
      <Typography.Paragraph type="secondary">{t('web.approvals.flows.fallback_hint')}</Typography.Paragraph>
      <ApproverFields
        name={['fallback']}
        path={['fallback']}
        form={form}
        resolvers={resolvers.data ?? []}
        facts={facts}
        required={false}
        label={t('web.approvals.flows.fallback_approver')}
      />
    </FormDrawer>
  );
};
