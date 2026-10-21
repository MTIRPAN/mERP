import type { Dayjs } from 'dayjs';
import type {
  EmploymentType,
  Gender,
  HireEmployeeCommand,
  HireEmployeeCommandCustomFields,
  MaritalStatus,
  MilitaryStatus,
} from '@/shared/api/generated/model';

/** Values of the hire wizard; the unit only narrows the position list. */
export interface HireFormValues {
  tckn?: string;
  firstName?: string;
  lastName?: string;
  birthDate?: Dayjs | null;
  birthPlace?: string;
  gender?: Gender;
  nationality?: string;
  maritalStatus?: MaritalStatus;
  militaryStatus?: MilitaryStatus;
  companyId?: string;
  hireDate?: Dayjs | null;
  employmentType?: EmploymentType;
  contractEndDate?: Dayjs | null;
  weeklyHours?: number | null;
  probationEndDate?: Dayjs | null;
  seniorityDate?: Dayjs | null;
  seniorityReason?: string;
  orgUnitId?: string;
  positionId?: string;
  branchId?: string;
  level?: string;
  costCenterCode?: string;
  customFields?: Record<string, unknown>;
}

/** The wizard's steps and the fields each one asks; API errors open the step of their field. */
export const hireSteps = [
  {
    key: 'person',
    fields: [
      'tckn',
      'firstName',
      'lastName',
      'birthDate',
      'birthPlace',
      'gender',
      'nationality',
      'maritalStatus',
      'militaryStatus',
    ],
  },
  {
    key: 'employment',
    fields: [
      'companyId',
      'hireDate',
      'employmentType',
      'contractEndDate',
      'weeklyHours',
      'probationEndDate',
      'seniorityDate',
      'seniorityReason',
    ],
  },
  {
    key: 'assignment',
    fields: ['orgUnitId', 'positionId', 'branchId', 'managerPositionId', 'level', 'costCenterCode', 'customFields'],
  },
] as const;

/** The step that shows <paramref name="field" />, e.g. "customFields.shoe_size" is on the assignment step. */
export const stepOfField = (field: string): number => {
  const root = field.split(/[.[]/)[0] ?? field;
  const index = hireSteps.findIndex((step) => (step.fields as readonly string[]).includes(root));
  return index < 0 ? 0 : index;
};

const day = (value: Dayjs | null | undefined) => (value ? value.format('YYYY-MM-DD') : null);

const text = (value: string | undefined) => (value?.trim() ? value.trim() : null);

/** The hire command of the wizard's values; the server checks every rule again. */
export const toHireCommand = (
  values: HireFormValues,
  customFields: HireEmployeeCommandCustomFields,
): HireEmployeeCommand => {
  const seniorityDate = day(values.seniorityDate);
  const hireDate = day(values.hireDate) ?? '';
  return {
    tckn: text(values.tckn),
    firstName: text(values.firstName),
    lastName: text(values.lastName),
    birthDate: day(values.birthDate),
    birthPlace: text(values.birthPlace),
    gender: values.gender ?? null,
    nationality: text(values.nationality)?.toUpperCase() ?? null,
    maritalStatus: values.maritalStatus ?? null,
    militaryStatus: values.militaryStatus ?? null,
    companyId: values.companyId ?? '',
    hireDate,
    employmentType: values.employmentType ?? null,
    contractEndDate: values.employmentType === 'FixedTerm' ? day(values.contractEndDate) : null,
    weeklyHours: values.employmentType === 'PartTime' ? (values.weeklyHours ?? null) : null,
    probationEndDate: day(values.probationEndDate),
    // Seniority starts on the hire date unless an earlier day is given with its reason
    seniorityDate: seniorityDate && seniorityDate !== hireDate ? seniorityDate : null,
    seniorityReason: seniorityDate && seniorityDate !== hireDate ? text(values.seniorityReason) : null,
    positionId: values.positionId ?? '',
    branchId: values.branchId ?? null,
    level: text(values.level),
    costCenterCode: text(values.costCenterCode),
    customFields,
  };
};
