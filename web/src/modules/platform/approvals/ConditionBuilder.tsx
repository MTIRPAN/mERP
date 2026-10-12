import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Button, DatePicker, Flex, Input, InputNumber, Segmented, Select, theme } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { ApprovalFactItem } from '@/shared/api/generated/model';
import {
  emptyGroup,
  factType,
  formatValue,
  MAX_GROUP_DEPTH,
  operatorsFor,
  parseValue,
  takesList,
  takesNoValue,
  type ConditionModel,
  type GroupModel,
  type Operator,
  type RuleModel,
} from './conditions';

interface ConditionBuilderProps {
  facts: ApprovalFactItem[];
  value?: GroupModel;
  onChange?: (value: GroupModel | undefined) => void;
  /** Text of the button that adds the first rule. */
  addLabel: string;
}

/**
 * Builds the JSON rule tree of §2.3 without code: "all" or "any" groups of rules on the document type's facts,
 * nested up to two levels. Works as an antd form control (value / onChange).
 */
export const ConditionBuilder = ({ facts, value, onChange, addLabel }: ConditionBuilderProps) => {
  if (!value) {
    return (
      <Button icon={<PlusOutlined />} onClick={() => onChange?.(emptyGroup())} disabled={facts.length === 0}>
        {addLabel}
      </Button>
    );
  }

  return (
    <GroupEditor
      facts={facts}
      group={value}
      depth={1}
      onChange={(group) => onChange?.(group)}
      onRemove={() => onChange?.(undefined)}
    />
  );
};

interface GroupEditorProps {
  facts: ApprovalFactItem[];
  group: GroupModel;
  depth: number;
  onChange: (group: GroupModel) => void;
  onRemove: () => void;
}

const GroupEditor = ({ facts, group, depth, onChange, onRemove }: GroupEditorProps) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const setItem = (index: number, item: ConditionModel) =>
    onChange({ ...group, items: group.items.map((current, i) => (i === index ? item : current)) });
  const removeItem = (index: number) => {
    const items = group.items.filter((_, i) => i !== index);
    if (items.length === 0) {
      onRemove();
    } else {
      onChange({ ...group, items });
    }
  };

  return (
    <div
      role="group"
      aria-label={t('web.approvals.condition.group')}
      style={{
        border: `1px solid ${token.colorBorderSecondary}`,
        borderRadius: token.borderRadius,
        padding: token.paddingSM,
        background: depth > 1 ? token.colorFillQuaternary : undefined,
      }}
    >
      <Flex justify="space-between" align="center" style={{ marginBottom: token.marginXS }}>
        <Segmented
          size="small"
          value={group.combinator}
          onChange={(combinator) => onChange({ ...group, combinator: combinator as GroupModel['combinator'] })}
          options={[
            { value: 'all', label: t('web.approvals.condition.all') },
            { value: 'any', label: t('web.approvals.condition.any') },
          ]}
        />
        <Button type="text" size="small" danger icon={<DeleteOutlined />} onClick={onRemove}>
          {t(depth === 1 ? 'web.approvals.condition.remove' : 'web.approvals.condition.remove_group')}
        </Button>
      </Flex>
      <Flex vertical gap={token.marginXS}>
        {group.items.map((item, index) =>
          item.kind === 'group' ? (
            <GroupEditor
              key={index}
              facts={facts}
              group={item}
              depth={depth + 1}
              onChange={(changed) => setItem(index, changed)}
              onRemove={() => removeItem(index)}
            />
          ) : (
            <RuleEditor
              key={index}
              facts={facts}
              rule={item}
              onChange={(changed) => setItem(index, changed)}
              onRemove={() => removeItem(index)}
            />
          ),
        )}
      </Flex>
      <Flex gap={token.marginXS} style={{ marginTop: token.marginXS }}>
        <Button
          size="small"
          icon={<PlusOutlined />}
          onClick={() => onChange({ ...group, items: [...group.items, { kind: 'rule' }] })}
        >
          {t('web.approvals.condition.add_rule')}
        </Button>
        {depth < MAX_GROUP_DEPTH && (
          <Button
            size="small"
            icon={<PlusOutlined />}
            onClick={() =>
              onChange({ ...group, items: [...group.items, emptyGroup(group.combinator === 'all' ? 'any' : 'all')] })
            }
          >
            {t('web.approvals.condition.add_group')}
          </Button>
        )}
      </Flex>
    </div>
  );
};

interface RuleEditorProps {
  facts: ApprovalFactItem[];
  rule: RuleModel;
  onChange: (rule: RuleModel) => void;
  onRemove: () => void;
}

const RuleEditor = ({ facts, rule, onChange, onRemove }: RuleEditorProps) => {
  const { t } = useTranslation();
  const type = factType(facts, rule.fact);
  const operators = operatorsFor(type);

  return (
    <Flex gap={8} align="center" wrap>
      <Select
        style={{ width: 180 }}
        placeholder={t('web.approvals.condition.fact')}
        aria-label={t('web.approvals.condition.fact')}
        value={rule.fact}
        options={facts.map((fact) => ({ value: fact.name, label: fact.label }))}
        onChange={(fact: string) => {
          // A new fact may not take the old operator or value
          const op = rule.op && operatorsFor(factType(facts, fact)).includes(rule.op) ? rule.op : undefined;
          onChange({ kind: 'rule', fact, op });
        }}
      />
      <Select<Operator>
        style={{ width: 160 }}
        placeholder={t('web.approvals.condition.operator')}
        aria-label={t('web.approvals.condition.operator')}
        value={rule.op}
        disabled={!rule.fact}
        options={operators.map((op) => ({ value: op, label: t(`web.approvals.operators.${op}`) }))}
        onChange={(op) =>
          onChange({
            ...rule,
            op,
            value: takesNoValue(op) ? undefined : takesList(op) === takesList(rule.op) ? rule.value : undefined,
          })
        }
      />
      {!takesNoValue(rule.op) && (
        <ValueInput rule={rule} type={type} onChange={(value) => onChange({ ...rule, value })} />
      )}
      <Button
        type="text"
        size="small"
        icon={<DeleteOutlined />}
        aria-label={t('web.approvals.condition.remove_rule')}
        onClick={onRemove}
      />
    </Flex>
  );
};

const ValueInput = ({
  rule,
  type,
  onChange,
}: {
  rule: RuleModel;
  type: ApprovalFactItem['type'] | undefined;
  onChange: (value: RuleModel['value']) => void;
}) => {
  const { t } = useTranslation();
  const label = t('web.approvals.condition.value');

  if (takesList(rule.op)) {
    return (
      <Input
        style={{ width: 220 }}
        aria-label={label}
        placeholder={t('web.approvals.condition.list_hint')}
        defaultValue={formatValue(rule.value)}
        onBlur={(event) => onChange(parseValue(event.target.value, type, rule.op))}
      />
    );
  }

  switch (type) {
    case 'Boolean':
      return (
        <Select<string>
          style={{ width: 120 }}
          aria-label={label}
          value={typeof rule.value === 'boolean' ? String(rule.value) : undefined}
          options={[
            { value: 'true', label: t('web.common.yes') },
            { value: 'false', label: t('web.common.no') },
          ]}
          onChange={(value) => onChange(value === 'true')}
        />
      );
    case 'Number':
      return (
        <InputNumber
          style={{ width: 140 }}
          aria-label={label}
          value={typeof rule.value === 'number' ? rule.value : null}
          onChange={(value) => onChange(value ?? undefined)}
        />
      );
    case 'Date':
      return (
        <DatePicker
          aria-label={label}
          format="DD.MM.YYYY"
          value={typeof rule.value === 'string' && rule.value ? dayjs(rule.value) : null}
          onChange={(date) => onChange(date ? date.format('YYYY-MM-DD') : undefined)}
        />
      );
    default:
      return (
        <Input
          style={{ width: 220 }}
          aria-label={label}
          value={typeof rule.value === 'string' ? rule.value : ''}
          onChange={(event) => onChange(event.target.value)}
        />
      );
  }
};
