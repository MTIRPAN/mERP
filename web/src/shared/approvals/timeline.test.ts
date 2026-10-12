import { describe, expect, it } from 'vitest';
import type { ApprovalHistoryItem, ApprovalRequestStepItem } from '@/shared/api/generated/model';
import { createTestI18n } from '@/test/render';
import { timelineComment, timelineText, timelineTone } from './timeline';

const t = createTestI18n({
  'web.approvals.timeline.approved_on_behalf': '{user}, {onBehalf} adına onayladı',
  'web.approvals.timeline.approved_overdue': '{step}: süre aşıldığı için sistem onayladı',
  'web.approvals.timeline.skipped': '{step} atlandı: {reason}',
  'web.approvals.timeline.skip_reasons.condition': 'adım koşulu sağlanmadı',
  'web.approvals.timeline.escalated': 'Süre aşıldı; görev {from} kullanıcısından {to} kullanıcısına aktarıldı',
}).t;

const steps: ApprovalRequestStepItem[] = [
  { index: 0, name: 'Yönetici', mode: 'Any', allowReturn: false, dueHours: 24 },
  { index: 1, name: 'İK', mode: 'Any', allowReturn: true, dueHours: null },
];

const entry = (item: Partial<ApprovalHistoryItem>): ApprovalHistoryItem => ({
  sequence: 1,
  kind: 'Approved',
  round: 1,
  step: 0,
  userId: null,
  userName: null,
  onBehalfOfUserId: null,
  onBehalfOfName: null,
  detail: null,
  detailUserName: null,
  at: '2026-10-12T09:00:00Z',
  ...item,
});

describe('timeline', () => {
  it('names the delegate and the approver they acted for', () => {
    const item = entry({
      userId: 'u2',
      userName: 'Ayşe',
      onBehalfOfUserId: 'u1',
      onBehalfOfName: 'Mehmet',
      detail: 'Uygun',
    });

    expect(timelineText(item, steps, t)).toBe('Ayşe, Mehmet adına onayladı');
    expect(timelineComment(item)).toBe('Uygun');
    expect(timelineTone(item)).toBe('success');
  });

  it('describes system decisions, skips and escalations without user comments', () => {
    const overdue = entry({ detail: 'overdue' });
    expect(timelineText(overdue, steps, t)).toBe('Yönetici: süre aşıldığı için sistem onayladı');
    expect(timelineComment(overdue)).toBeNull();

    expect(timelineText(entry({ kind: 'Skipped', step: 1, detail: 'condition' }), steps, t)).toBe(
      'İK atlandı: adım koşulu sağlanmadı',
    );

    const escalated = entry({ kind: 'Escalated', onBehalfOfName: 'Mehmet', detail: 'u3', detailUserName: 'Zeynep' });
    expect(timelineText(escalated, steps, t)).toBe(
      'Süre aşıldı; görev Mehmet kullanıcısından Zeynep kullanıcısına aktarıldı',
    );
    expect(timelineTone(escalated)).toBe('warning');
  });
});
