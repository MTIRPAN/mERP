import type {
  ApprovalDefinitionDetails,
  ApprovalEscalation,
  ApprovalMode,
  ApprovalStepInput,
  ApproverRuleInput,
} from '@/shared/api/generated/model';
import { fromJson, toJson, type GroupModel } from './conditions';

export interface RuleValues {
  resolver?: string;
  parameters?: Record<string, string | undefined>;
}

export interface StepValues {
  /** Name in the default culture; the other cultures are kept in `names`. */
  name?: string;
  names?: Record<string, string>;
  approver?: RuleValues;
  mode: ApprovalMode;
  condition?: GroupModel;
  dueHours?: number | null;
  escalation: ApprovalEscalation;
  skipIfApproverIsRequester: boolean;
  skipIfAlreadyApproved: boolean;
  allowReturn: boolean;
}

export interface FlowValues {
  name?: Record<string, string | undefined>;
  companyId?: string;
  priority?: number;
  isActive?: boolean;
  entryCondition?: GroupModel;
  steps?: StepValues[];
  fallback?: RuleValues;
}

export const newStep = (): StepValues => ({
  mode: 'Any',
  escalation: 'None',
  skipIfApproverIsRequester: true,
  skipIfAlreadyApproved: true,
  allowReturn: false,
});

const texts = (values: Record<string, string | undefined> | undefined): Record<string, string> =>
  Object.fromEntries(
    Object.entries(values ?? {}).flatMap(([culture, text]) => (text?.trim() ? [[culture, text.trim()]] : [])),
  );

const rule = (values: RuleValues | undefined): ApproverRuleInput | null =>
  values?.resolver ? { resolver: values.resolver, parameters: texts(values.parameters) } : null;

/** A definition → form values; step names are edited in the default culture. */
export const toFormValues = (definition: ApprovalDefinitionDetails, defaultCulture: string): FlowValues => ({
  name: definition.name,
  companyId: definition.companyId ?? undefined,
  priority: definition.priority,
  isActive: definition.isActive,
  entryCondition: fromJson(definition.entryCondition),
  fallback: definition.fallback
    ? { resolver: definition.fallback.resolver, parameters: definition.fallback.parameters }
    : undefined,
  steps: definition.steps.map((step) => ({
    name: step.name[defaultCulture],
    names: step.name,
    approver: { resolver: step.approver.resolver, parameters: step.approver.parameters },
    mode: step.mode,
    condition: fromJson(step.condition),
    dueHours: step.dueHours,
    escalation: step.escalation,
    skipIfApproverIsRequester: step.skipIfApproverIsRequester,
    skipIfAlreadyApproved: step.skipIfAlreadyApproved,
    allowReturn: step.allowReturn,
  })),
});

/** Form values → the steps, conditions and fallback of a create or update command. */
export const toCommand = (values: FlowValues, defaultCulture: string) => ({
  name: texts(values.name),
  priority: values.priority ?? 0,
  entryCondition: toJson(values.entryCondition),
  fallback: rule(values.fallback),
  steps: (values.steps ?? []).map((step): ApprovalStepInput => ({
    name: { ...(step.names ?? {}), ...texts({ [defaultCulture]: step.name }) },
    approver: rule(step.approver),
    mode: step.mode,
    condition: toJson(step.condition),
    dueHours: step.dueHours ?? null,
    escalation: step.escalation,
    skipIfApproverIsRequester: step.skipIfApproverIsRequester,
    skipIfAlreadyApproved: step.skipIfAlreadyApproved,
    allowReturn: step.allowReturn,
  })),
});
