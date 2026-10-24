import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Button, Card, Col, Empty, Flex, Form, Input, Row, Select, Skeleton, Space, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useActiveCompanyId } from '@/app/company/companyStore';
import { useMe } from '@/app/me/meContext';
import {
  getGetLeavePolicyRulesQueryKey,
  useGetLeavePolicyRules,
  useListLeavePolicies,
  useReplaceLeavePolicyRules,
} from '@/shared/api/generated/endpoints/hr/hr';
import { CriterionKind, type LeavePolicyRuleInput } from '@/shared/api/generated/model';
import { applyApiErrorsToForm } from '@/shared/api/formErrors';
import { CriterionFields } from '../CriterionFields';

interface RulesFormValues {
  rules?: LeavePolicyRuleInput[];
}

/**
 * The policy assignment rules of a company in priority order (D41): the first rule whose criteria all match gives an
 * employee's policy; an employee no rule matches gets the company's default, else every company's.
 */
export const PolicyRulesTab = ({ canManage }: { canManage: boolean }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const me = useMe();
  const activeCompanyId = useActiveCompanyId();
  const [companyId, setCompanyId] = useState<string | undefined>(activeCompanyId ?? me.companies[0]?.id);
  const [form] = Form.useForm<RulesFormValues>();
  const queryClient = useQueryClient();
  const rules = useGetLeavePolicyRules(companyId ?? '', { query: { enabled: !!companyId } });
  const policies = useListLeavePolicies({ companyId }, { query: { enabled: !!companyId } });
  const replace = useReplaceLeavePolicyRules();
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (rules.data) {
      form.setFieldsValue({
        rules: rules.data.rules.map((r) => ({ name: r.name, policyId: r.policyId, criteria: r.criteria })),
      });
    }
  }, [rules.data, form]);

  const save = async (values: RulesFormValues) => {
    if (!companyId) {
      return;
    }

    setSaving(true);
    try {
      await replace.mutateAsync({
        companyId,
        data: { version: rules.data?.version ?? null, rules: values.rules ?? [] },
      });
      await queryClient.invalidateQueries({ queryKey: getGetLeavePolicyRulesQueryKey(companyId) });
      message.success(t('web.hr.leave.rules_tab.saved'));
    } catch (error) {
      if (!applyApiErrorsToForm(form, error)) {
        throw error;
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <Flex vertical gap={12}>
      <Flex justify="space-between" align="center" gap={8} wrap>
        <Select
          aria-label={t('web.hr.leave.fields.company')}
          value={companyId}
          onChange={setCompanyId}
          options={me.companies.map((c) => ({ value: c.id, label: c.name }))}
          style={{ minWidth: 240 }}
        />
        <Typography.Text type="secondary">{t('web.hr.leave.rules_tab.hint')}</Typography.Text>
      </Flex>
      {!companyId ? (
        <Empty description={t('web.hr.leave.rules_tab.pick_company')} />
      ) : rules.isLoading ? (
        <Skeleton active />
      ) : (
        <Form<RulesFormValues>
          form={form}
          layout="vertical"
          onFinish={save}
          disabled={!canManage}
          requiredMark="optional"
        >
          <Form.List name="rules">
            {(rows, { add, remove, move }) => (
              <Flex vertical gap={12}>
                {rows.length === 0 && (
                  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.hr.leave.rules_tab.empty')} />
                )}
                {rows.map((row, index) => (
                  <Card
                    key={row.key}
                    size="small"
                    title={t('web.hr.leave.rules_tab.position', { position: index + 1 })}
                    extra={
                      canManage && (
                        <Space size={4}>
                          <Button
                            size="small"
                            icon={<ArrowUpOutlined />}
                            disabled={index === 0}
                            onClick={() => move(index, index - 1)}
                            aria-label={t('web.hr.leave.rules_tab.up')}
                          />
                          <Button
                            size="small"
                            icon={<ArrowDownOutlined />}
                            disabled={index === rows.length - 1}
                            onClick={() => move(index, index + 1)}
                            aria-label={t('web.hr.leave.rules_tab.down')}
                          />
                          <Button
                            size="small"
                            danger
                            icon={<DeleteOutlined />}
                            onClick={() => remove(row.name)}
                            aria-label={t('web.common.delete')}
                          />
                        </Space>
                      )
                    }
                  >
                    <Row gutter={12}>
                      <Col xs={24} md={12}>
                        <Form.Item
                          name={[row.name, 'name']}
                          label={t('web.hr.leave.rules_tab.name')}
                          rules={[{ required: true, message: t('web.platform.fields.required') }]}
                        >
                          <Input maxLength={100} />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={12}>
                        <Form.Item
                          name={[row.name, 'policyId']}
                          label={t('web.hr.leave.rules_tab.policy')}
                          rules={[{ required: true, message: t('web.platform.fields.required') }]}
                        >
                          <Select
                            showSearch={{ optionFilterProp: 'label' }}
                            loading={policies.isFetching}
                            options={(policies.data ?? []).map((p) => ({
                              value: p.id,
                              label: `${p.name} (${p.code})`,
                            }))}
                          />
                        </Form.Item>
                      </Col>
                    </Row>
                    <Form.List name={[row.name, 'criteria']}>
                      {(criteria, { add: addCriterion, remove: removeCriterion }) => (
                        <Flex vertical gap={4}>
                          {criteria.length === 0 && (
                            <Typography.Text type="secondary">{t('web.hr.leave.rules_tab.everyone')}</Typography.Text>
                          )}
                          {criteria.map((criterion) => (
                            <Flex key={criterion.key} gap={8} align="flex-start">
                              <div style={{ flex: 1 }}>
                                <CriterionFields
                                  name={['rules', row.name, 'criteria', criterion.name]}
                                  field={[criterion.name]}
                                  companyId={companyId}
                                />
                              </div>
                              {canManage && (
                                <Button
                                  type="text"
                                  danger
                                  icon={<DeleteOutlined />}
                                  onClick={() => removeCriterion(criterion.name)}
                                  style={{ marginTop: 30 }}
                                  aria-label={t('web.common.delete')}
                                />
                              )}
                            </Flex>
                          ))}
                          {canManage && (
                            <Button
                              size="small"
                              icon={<PlusOutlined />}
                              onClick={() => addCriterion({ kind: CriterionKind.EmploymentType, values: [] })}
                              style={{ alignSelf: 'flex-start' }}
                            >
                              {t('web.hr.leave.rules_tab.add_criterion')}
                            </Button>
                          )}
                        </Flex>
                      )}
                    </Form.List>
                  </Card>
                ))}
                {canManage && (
                  <Flex justify="space-between" gap={8}>
                    <Button icon={<PlusOutlined />} onClick={() => add({ name: '', criteria: [] })}>
                      {t('web.hr.leave.rules_tab.add')}
                    </Button>
                    <Button type="primary" htmlType="submit" loading={saving}>
                      {t('web.common.save')}
                    </Button>
                  </Flex>
                )}
              </Flex>
            )}
          </Form.List>
        </Form>
      )}
    </Flex>
  );
};
