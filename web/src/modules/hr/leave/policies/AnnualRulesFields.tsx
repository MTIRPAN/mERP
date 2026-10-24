import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, Card, Col, Divider, Flex, Form, InputNumber, Row, Select, Switch, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { AccrualMethod, CriterionKind, LeaveRounding, TenDayBlockRule } from '@/shared/api/generated/model';
import { enumOptions } from '../../employees/forms/formValues';
import { CriterionFields } from '../CriterionFields';

interface AnnualRulesFieldsProps {
  /** Path of the rules in the form, e.g. ['versions', 0, 'rules']. */
  name: (string | number)[];
  /** A version that has started: shown, not changed (D41). */
  locked: boolean;
  /** The policy's company, for the branches and units of extra day rules. */
  companyId?: string;
}

const Days = ({ disabled, min = 0, max = 366 }: { disabled: boolean; min?: number; max?: number }) => (
  <InputNumber min={min} max={max} step={0.5} disabled={disabled} style={{ width: '100%' }} />
);

/**
 * The annual leave rules of a policy version (docs/domain/izin-kurallari.md §2): the tiers, the age minimums, the
 * extra day rules, accrual, advance, rounding and the request rules.
 */
export const AnnualRulesFields = ({ name, locked, companyId }: AnnualRulesFieldsProps) => {
  const { t } = useTranslation();
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const flag = (key: string, label: string, extra?: string) => (
    <Form.Item name={[...name, key]} label={label} extra={extra} valuePropName="checked">
      <Switch disabled={locked} />
    </Form.Item>
  );

  return (
    <Flex vertical gap={8}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        {t('web.hr.leave.rules.entitlement')}
      </Typography.Title>
      <Row gutter={12}>
        <Col xs={24} md={8}>
          <Form.Item
            name={[...name, 'minServiceYearsForEntitlement']}
            label={t('web.hr.leave.rules.min_service')}
            rules={required}
          >
            <InputNumber
              min={0}
              max={50}
              disabled={locked}
              style={{ width: '100%' }}
              addonAfter={t('web.hr.leave.rules.years')}
            />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name={[...name, 'accrualMethod']} label={t('web.hr.leave.rules.accrual_method')} rules={required}>
            <Select disabled={locked} options={enumOptions(AccrualMethod, 'web.hr.leave.accrual_method.', t)} />
          </Form.Item>
        </Col>
      </Row>
      <Form.List name={[...name, 'tiers']}>
        {(rows, { add, remove }) => (
          <Card
            size="small"
            title={t('web.hr.leave.rules.tiers')}
            extra={
              !locked && (
                <Button size="small" icon={<PlusOutlined />} onClick={() => add({})}>
                  {t('web.hr.leave.rules.add_tier')}
                </Button>
              )
            }
          >
            {rows.map((row) => (
              <Row key={row.key} gutter={12} align="bottom">
                <Col span={10}>
                  <Form.Item
                    name={[row.name, 'minServiceYears']}
                    label={t('web.hr.leave.rules.from_years')}
                    rules={required}
                  >
                    <InputNumber min={0} max={60} disabled={locked} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={10}>
                  <Form.Item name={[row.name, 'days']} label={t('web.hr.leave.rules.days')} rules={required}>
                    <Days disabled={locked} />
                  </Form.Item>
                </Col>
                <Col span={4}>
                  {!locked && rows.length > 1 && (
                    <Form.Item>
                      <Button
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => remove(row.name)}
                        aria-label={t('web.common.delete')}
                      />
                    </Form.Item>
                  )}
                </Col>
              </Row>
            ))}
          </Card>
        )}
      </Form.List>
      <Form.List name={[...name, 'ageMinimums']}>
        {(rows, { add, remove }) => (
          <Card
            size="small"
            title={t('web.hr.leave.rules.age_minimums')}
            extra={
              !locked && (
                <Button size="small" icon={<PlusOutlined />} onClick={() => add({})}>
                  {t('web.hr.leave.rules.add_age')}
                </Button>
              )
            }
          >
            {rows.length === 0 && <Typography.Text type="secondary">{t('web.hr.leave.rules.none')}</Typography.Text>}
            {rows.map((row) => (
              <Row key={row.key} gutter={12} align="bottom">
                <Col span={7}>
                  <Form.Item name={[row.name, 'maxAge']} label={t('web.hr.leave.rules.max_age')}>
                    <InputNumber min={0} max={100} disabled={locked} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={7}>
                  <Form.Item name={[row.name, 'minAge']} label={t('web.hr.leave.rules.min_age')}>
                    <InputNumber min={0} max={100} disabled={locked} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col span={7}>
                  <Form.Item name={[row.name, 'minDays']} label={t('web.hr.leave.rules.min_days')} rules={required}>
                    <Days disabled={locked} />
                  </Form.Item>
                </Col>
                <Col span={3}>
                  {!locked && (
                    <Form.Item>
                      <Button
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => remove(row.name)}
                        aria-label={t('web.common.delete')}
                      />
                    </Form.Item>
                  )}
                </Col>
              </Row>
            ))}
          </Card>
        )}
      </Form.List>
      <Form.List name={[...name, 'extraDayRules']}>
        {(rows, { add, remove }) => (
          <Card
            size="small"
            title={t('web.hr.leave.rules.extra_days')}
            extra={
              !locked && (
                <Button
                  size="small"
                  icon={<PlusOutlined />}
                  onClick={() => add({ criterion: { kind: CriterionKind.JobLevel, values: [] } })}
                >
                  {t('web.hr.leave.rules.add_extra')}
                </Button>
              )
            }
          >
            {rows.length === 0 && <Typography.Text type="secondary">{t('web.hr.leave.rules.none')}</Typography.Text>}
            {rows.map((row, index) => (
              <div key={row.key}>
                {index > 0 && <Divider style={{ margin: '8px 0' }} />}
                <fieldset disabled={locked} style={{ border: 0, padding: 0, margin: 0 }}>
                  <CriterionFields
                    name={[...name, 'extraDayRules', row.name, 'criterion']}
                    field={[row.name, 'criterion']}
                    companyId={companyId}
                  />
                </fieldset>
                <Row gutter={12} align="bottom">
                  <Col span={8}>
                    <Form.Item
                      name={[row.name, 'days']}
                      label={t('web.hr.leave.rules.extra_days_count')}
                      rules={required}
                    >
                      <Days disabled={locked} />
                    </Form.Item>
                  </Col>
                  <Col span={4}>
                    {!locked && (
                      <Form.Item>
                        <Button
                          type="text"
                          danger
                          icon={<DeleteOutlined />}
                          onClick={() => remove(row.name)}
                          aria-label={t('web.common.delete')}
                        />
                      </Form.Item>
                    )}
                  </Col>
                </Row>
              </div>
            ))}
          </Card>
        )}
      </Form.List>

      <Typography.Title level={5} style={{ margin: '8px 0 0' }}>
        {t('web.hr.leave.rules.usage')}
      </Typography.Title>
      <Row gutter={12}>
        <Col xs={24} md={8}>
          <Form.Item
            name={[...name, 'tenDayBlockRule']}
            label={t('web.hr.leave.rules.ten_day_block')}
            tooltip={t('web.hr.leave.rules.ten_day_block_hint')}
            rules={required}
          >
            <Select disabled={locked} options={enumOptions(TenDayBlockRule, 'web.hr.leave.ten_day_block.', t)} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name={[...name, 'rounding']} label={t('web.hr.leave.rules.rounding')} rules={required}>
            <Select disabled={locked} options={enumOptions(LeaveRounding, 'web.hr.leave.rounding.', t)} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item
            name={[...name, 'advanceLimitDays']}
            label={t('web.hr.leave.rules.advance')}
            tooltip={t('web.hr.leave.rules.advance_hint')}
            rules={required}
          >
            <Days disabled={locked} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item
            name={[...name, 'carryOverLimitDays']}
            label={t('web.hr.leave.rules.carry_over')}
            tooltip={t('web.hr.leave.rules.carry_over_hint')}
          >
            <Days disabled={locked} max={999} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name={[...name, 'maxTravelDays']} label={t('web.hr.leave.rules.max_travel_days')} rules={required}>
            <InputNumber min={0} max={30} disabled={locked} style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name={[...name, 'minNoticeDays']} label={t('web.hr.leave.rules.min_notice')}>
            <InputNumber min={0} max={365} disabled={locked} style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col xs={24} md={8}>
          <Form.Item name={[...name, 'maxDaysPerRequest']} label={t('web.hr.leave.rules.max_per_request')}>
            <Days disabled={locked} min={0.5} max={999} />
          </Form.Item>
        </Col>
      </Row>
      <Row gutter={12}>
        <Col xs={12} md={6}>
          {flag('allowTravelDays', t('web.hr.leave.rules.allow_travel_days'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('allowHalfDay', t('web.hr.leave.rules.allow_half_day'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('allowHourly', t('web.hr.leave.rules.allow_hourly'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('allowBeforeFirstEntitlement', t('web.hr.leave.rules.allow_before_first'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('useGroupSeniority', t('web.hr.leave.rules.group_seniority'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('unpaidLeaveShiftsAccrualDate', t('web.hr.leave.rules.unpaid_shifts'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('transferBalanceOnGroupTransfer', t('web.hr.leave.rules.transfer_balance'))}
        </Col>
        <Col xs={12} md={6}>
          {flag('allowPastDatesForHr', t('web.hr.leave.rules.past_dates'))}
        </Col>
      </Row>
    </Flex>
  );
};
