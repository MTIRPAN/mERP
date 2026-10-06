import type { TFunction } from 'i18next';
import type { AuditItem } from '@/shared/api/generated/model';

/** Value the API writes for a [Sensitive] field (backend AuditLogInterceptor.MaskedValue). */
export const MASKED_VALUE = '***';

export type AuditAction = 'Insert' | 'Update' | 'Delete' | 'Read';

export interface AuditChange {
  id: string;
  field: string | null;
  label: string | null;
  oldValue: string | null;
  newValue: string | null;
}

/** The field changes of one record made by one operation (same request, user, moment and action). */
export interface AuditOperation {
  key: string;
  occurredAt: string;
  userName: string | null;
  action: AuditAction;
  entityLabel: string;
  entityId: string;
  changes: AuditChange[];
}

const operationKey = (item: AuditItem) =>
  [item.correlationId ?? item.id, item.occurredAt, item.userId ?? '', item.entityType, item.entityId, item.action].join(
    '|',
  );

/** Groups entries (newest first, as the API returns them) into operations, keeping the order. */
export const groupOperations = (items: AuditItem[]): AuditOperation[] => {
  const operations: AuditOperation[] = [];
  for (const item of items) {
    const key = operationKey(item);
    const last = operations.at(-1);
    const change = {
      id: item.id,
      field: item.field,
      label: item.fieldLabel,
      oldValue: item.oldValue,
      newValue: item.newValue,
    };
    if (last?.key === key) {
      last.changes.push(change);
    } else {
      operations.push({
        key,
        occurredAt: item.occurredAt,
        userName: item.userName,
        action: item.action as AuditAction,
        entityLabel: item.entityLabel,
        entityId: item.entityId,
        changes: [change],
      });
    }
  }

  return operations;
};

/** Localized texts are stored as JSON ({"tr-TR": "…"}); show the texts, not the JSON. */
const localizedText = (value: string): string | undefined => {
  if (!value.startsWith('{')) {
    return undefined;
  }

  try {
    const parsed: unknown = JSON.parse(value);
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      const texts = Object.values(parsed as Record<string, unknown>).filter((v) => typeof v === 'string');
      return texts.length ? texts.join(' / ') : undefined;
    }
  } catch {
    // Not JSON: shown as it is
  }

  return undefined;
};

/** A stored value as the user should read it: booleans in words, masked values explained, empty as a dash. */
export const displayValue = (value: string | null, t: TFunction): string => {
  if (value === null || value === '') {
    return '—';
  }

  if (value === MASKED_VALUE) {
    return t('web.audit.masked');
  }

  if (value === 'True' || value === 'False') {
    return t(value === 'True' ? 'web.common.yes' : 'web.common.no');
  }

  return localizedText(value) ?? value;
};

export const actionColors: Record<AuditAction, string> = {
  Insert: 'success',
  Update: 'processing',
  Delete: 'error',
  Read: 'warning',
};
