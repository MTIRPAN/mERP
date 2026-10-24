import { DeleteOutlined, LockOutlined, PlusOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Card,
  Col,
  DatePicker,
  Flex,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Switch,
  Tooltip,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  getListLeaveTypesQueryKey,
  useCreateLeaveType,
  useUpdateLeaveType,
} from '@/shared/api/generated/endpoints/hr/hr';
import {
  DurationBasis,
  Gender,
  LeaveCategory,
  LeavePayer,
  LeaveUnit,
  type LeaveTypeItem,
  type LeaveTypeVersion,
} from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { enumOptions } from '../../employees/forms/formValues';
import { CompanyField } from '../CompanyField';
import { isBeginning, isStarted, numberOrNull, toValidFrom, tomorrow, versionLabel } from '../leaveVersions';
import { LegalWarnings } from '../LegalWarnings';
import { ALL_COMPANIES, nameAliases, toNames } from '../leaveForms';
import { NameFields } from '../NameFields';

const LIMITS = [
  'fixedDays',
  'daysBeforeEvent',
  'multipleBirthExtraDays',
  'maxDaysPerYear',
  'maxDaysPerRequest',
  'dailyHours',
  'minNoticeDays',
] as const;

type Limit = (typeof LIMITS)[number];

/** A version row: saved versions keep their date as text, new ones pick a day. */
interface VersionValues extends Partial<Record<Limit, number | null>> {
  validFrom?: Dayjs | string | null;
}

interface LeaveTypeFormValues {
  code?: string;
  name?: Record<string, string>;
  companyId?: string;
  category?: LeaveCategory;
  payer?: LeavePayer;
  unit?: LeaveUnit;
  durationBasis?: DurationBasis;
  deductsFrom?: string;
  requiresDocument?: boolean;
  requiresApproval?: boolean;
  genderRestriction?: Gender | null;
  allowPastDates?: boolean;
  legalBasis?: string;
  isActive?: boolean;
  versions?: VersionValues[];
}

interface LeaveTypeDrawerProps {
  type?: LeaveTypeItem;
  /** Codes of the tenant's types, for the balance a type uses. */
  codes: string[];
  onClose: () => void;
}

const toVersion = (values: VersionValues): LeaveTypeVersion => ({
  validFrom: toValidFrom(values.validFrom),
  ...Object.fromEntries(LIMITS.map((limit) => [limit, numberOrNull(values[limit])])),
});

/**
 * A leave type and its versioned limits (D41). Versions that have started are shown locked; a new version starts
 * tomorrow at the earliest.
 */
export const LeaveTypeDrawer = ({ type, codes, onClose }: LeaveTypeDrawerProps) => {
  const { t, i18n } = useTranslation();
  const [form] = Form.useForm<LeaveTypeFormValues>();
  const queryClient = useQueryClient();
  const create = useCreateLeaveType();
  const update = useUpdateLeaveType();
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const saved = new Set((type?.versions ?? []).filter((v) => isStarted(v.validFrom)).map((v) => v.validFrom));

  const submit = async (values: LeaveTypeFormValues) => {
    const fields = {
      name: toNames(values.name),
      category: values.category ?? null,
      payer: values.payer ?? null,
      unit: values.unit ?? null,
      durationBasis: values.durationBasis ?? null,
      deductsFrom: values.deductsFrom ?? null,
      requiresDocument: !!values.requiresDocument,
      requiresApproval: !!values.requiresApproval,
      genderRestriction: values.genderRestriction ?? null,
      allowPastDates: !!values.allowPastDates,
      legalBasis: values.legalBasis?.trim() || null,
      versions: (values.versions ?? []).map(toVersion),
    };
    if (type) {
      await update.mutateAsync({
        id: type.id,
        data: { ...fields, version: type.version, isActive: !!values.isActive },
      });
    } else {
      await create.mutateAsync({
        data: { ...fields, code: values.code?.trim().toUpperCase() ?? null, companyId: values.companyId || null },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListLeaveTypesQueryKey() });
  };

  return (
    <FormDrawer<LeaveTypeFormValues>
      title={t(type ? 'web.hr.leave.types.edit' : 'web.hr.leave.types.new')}
      open
      onClose={onClose}
      onSubmit={submit}
      form={form}
      width={820}
      fieldAliases={nameAliases}
      initialValues={
        type
          ? {
              ...type,
              name: { ...type.names },
              companyId: type.companyId ?? ALL_COMPANIES,
              deductsFrom: type.deductsFrom ?? undefined,
              legalBasis: type.legalBasis ?? undefined,
              versions: type.versions,
            }
          : {
              companyId: ALL_COMPANIES,
              payer: LeavePayer.Employer,
              unit: LeaveUnit.Day,
              durationBasis: DurationBasis.WorkingDays,
              requiresApproval: true,
              allowPastDates: true,
              versions: [{ validFrom: null }],
            }
      }
    >
      {type && <LegalWarnings warnings={type.warnings} />}
      <Row gutter={12}>
        <Col span={8}>
          <Form.Item
            name="code"
            label={t('web.platform.fields.code')}
            tooltip={t('web.platform.fields.code_hint')}
            rules={required}
          >
            <Input maxLength={30} disabled={!!type} style={{ textTransform: 'uppercase' }} />
          </Form.Item>
        </Col>
        <Col span={16}>
          <CompanyField disabled={!!type} />
        </Col>
      </Row>
      <NameFields />
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="category" label={t('web.hr.leave.fields.category')} rules={required}>
            <Select options={enumOptions(LeaveCategory, 'web.hr.leave.category.', t)} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="payer" label={t('web.hr.leave.fields.payer')} rules={required}>
            <Select options={enumOptions(LeavePayer, 'web.hr.leave.payer.', t)} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="durationBasis" label={t('web.hr.leave.fields.duration_basis')} rules={required}>
            <Select options={enumOptions(DurationBasis, 'web.hr.leave.duration_basis.', t)} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="unit" label={t('web.hr.leave.fields.unit')} rules={required}>
            <Select options={enumOptions(LeaveUnit, 'web.hr.leave.unit.', t)} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item
            name="deductsFrom"
            label={t('web.hr.leave.fields.deducts_from')}
            tooltip={t('web.hr.leave.fields.deducts_from_hint')}
          >
            <Select allowClear options={codes.map((code) => ({ value: code, label: code }))} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="genderRestriction" label={t('web.hr.leave.fields.gender')}>
            <Select allowClear options={enumOptions(Gender, 'web.hr.gender.', t)} />
          </Form.Item>
        </Col>
        <Col span={16}>
          <Form.Item name="legalBasis" label={t('web.hr.leave.fields.legal_basis')}>
            <Input maxLength={200} />
          </Form.Item>
        </Col>
      </Row>
      <Flex gap={24} wrap>
        <Form.Item name="requiresApproval" label={t('web.hr.leave.fields.requires_approval')} valuePropName="checked">
          <Switch />
        </Form.Item>
        <Form.Item name="requiresDocument" label={t('web.hr.leave.fields.requires_document')} valuePropName="checked">
          <Switch />
        </Form.Item>
        <Form.Item name="allowPastDates" label={t('web.hr.leave.fields.allow_past_dates')} valuePropName="checked">
          <Switch />
        </Form.Item>
        {type && (
          <Form.Item name="isActive" label={t('web.platform.fields.status')} valuePropName="checked">
            <Switch />
          </Form.Item>
        )}
      </Flex>
      <Typography.Title level={5}>{t('web.hr.leave.versions.title')}</Typography.Title>
      <Typography.Paragraph type="secondary">{t('web.hr.leave.versions.hint')}</Typography.Paragraph>
      <Form.List name="versions">
        {(rows, { add, remove }) => (
          <Flex vertical gap={12}>
            {rows.map((row) => {
              const validFrom = form.getFieldValue(['versions', row.name, 'validFrom']) as
                Dayjs | string | null | undefined;
              const locked = typeof validFrom === 'string' && saved.has(validFrom);
              return (
                <Card
                  key={row.key}
                  size="small"
                  title={
                    locked ? (
                      <Tooltip title={t('web.hr.leave.versions.locked')}>
                        <span>
                          <LockOutlined /> {versionLabel(validFrom, t, i18n.language)}
                        </span>
                      </Tooltip>
                    ) : (
                      <VersionDate row={row.name} first={row.name === 0 && !type} />
                    )
                  }
                  extra={
                    !locked && rows.length > 1 ? (
                      <Button
                        size="small"
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => remove(row.name)}
                        aria-label={t('web.common.delete')}
                      />
                    ) : undefined
                  }
                >
                  <Row gutter={12}>
                    {LIMITS.map((limit) => (
                      <Col key={limit} xs={12} md={6}>
                        <Form.Item name={[row.name, limit]} label={t(`web.hr.leave.limits.${limit}`)}>
                          <InputNumber
                            min={0}
                            max={limit === 'dailyHours' ? 24 : 999}
                            step={0.5}
                            disabled={locked}
                            style={{ width: '100%' }}
                          />
                        </Form.Item>
                      </Col>
                    ))}
                  </Row>
                </Card>
              );
            })}
            <Button
              icon={<PlusOutlined />}
              onClick={() => add({ validFrom: tomorrow() })}
              style={{ alignSelf: 'flex-start' }}
            >
              {t('web.hr.leave.versions.add')}
            </Button>
          </Flex>
        )}
      </Form.List>
    </FormDrawer>
  );
};

/**
 * The first day of a version still to come. The first version of a new type may stay empty: in force from the
 * beginning.
 */
export const VersionDate = ({ row, first }: { row: number; first: boolean }) => {
  const { t } = useTranslation();
  return (
    <Form.Item
      name={[row, 'validFrom']}
      noStyle
      getValueProps={(value: Dayjs | string | null | undefined) => ({
        value: typeof value === 'string' ? (isBeginning(value) ? null : dayjs(value)) : value,
      })}
      rules={first ? undefined : [{ required: true, message: t('web.platform.fields.required') }]}
    >
      <DatePicker
        format="L"
        allowClear={first}
        placeholder={first ? t('web.hr.leave.versions.from_beginning') : t('web.hr.leave.versions.pick_date')}
        disabledDate={first ? undefined : (day) => day.isBefore(tomorrow(), 'day')}
      />
    </Form.Item>
  );
};
