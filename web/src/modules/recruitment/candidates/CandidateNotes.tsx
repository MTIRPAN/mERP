import { DeleteOutlined } from '@ant-design/icons';
import { App, Button, Empty, Flex, List, Mentions, Popconfirm, Space, Tag, Typography } from 'antd';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useSearchCandidateMentionUsers,
  useSearchMyHiringMentionUsers,
} from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CandidateNoteItem } from '@/shared/api/generated/model';
import { isApiError } from '@/shared/api/problem';
import { formatDateTime } from '@/shared/format/date';
import { addNote, mentionedIn, removeNote, useCandidateRefresh } from './candidates';

interface CandidateNotesProps {
  candidateId: string;
  notes: CandidateNoteItem[];
  /** The hiring team notes through its own endpoints. */
  team: boolean;
  canWrite: boolean;
}

/** The team's notes on a candidate (D50); <c>@name</c> mentions notify the users named. */
export const CandidateNotes = ({ candidateId, notes, team, canWrite }: CandidateNotesProps) => {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const refresh = useCandidateRefresh();
  const [body, setBody] = useState('');
  const [search, setSearch] = useState<string>();
  const [saving, setSaving] = useState(false);
  const picked = useRef(new Map<string, string>());
  const recruiterUsers = useSearchCandidateMentionUsers({ search }, { query: { enabled: canWrite && !team } });
  const teamUsers = useSearchMyHiringMentionUsers({ search }, { query: { enabled: canWrite && team } });
  const users = (team ? teamUsers : recruiterUsers).data ?? [];

  const report = (error: unknown) =>
    void message.error(isApiError(error) ? (error.problem?.fieldErrors?.[0]?.message ?? error.message) : String(error));

  const save = async () => {
    setSaving(true);
    try {
      await addNote(candidateId, team, body.trim(), mentionedIn(body, picked.current));
      setBody('');
      picked.current.clear();
      await refresh(candidateId);
    } catch (error) {
      report(error);
    } finally {
      setSaving(false);
    }
  };

  return (
    <Flex vertical gap={12}>
      {canWrite && (
        <Flex vertical gap={8}>
          <Mentions
            aria-label={t('web.recruitment.candidates.notes.new')}
            placeholder={t('web.recruitment.candidates.notes.placeholder')}
            value={body}
            onChange={setBody}
            onSearch={(text) => setSearch(text || undefined)}
            onSelect={(option) => {
              if (option.value && option.key) {
                picked.current.set(option.value, option.key);
              }
            }}
            options={users.map((user) => ({ key: user.id, value: user.displayName, label: user.displayName }))}
            maxLength={2000}
            autoSize={{ minRows: 2, maxRows: 6 }}
          />
          <Button
            type="primary"
            loading={saving}
            disabled={!body.trim()}
            onClick={() => void save()}
            style={{ alignSelf: 'flex-end' }}
          >
            {t('web.recruitment.candidates.notes.add')}
          </Button>
        </Flex>
      )}
      <List
        dataSource={notes}
        locale={{
          emptyText: (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('web.recruitment.candidates.notes.empty')} />
          ),
        }}
        renderItem={(note) => (
          <List.Item
            key={note.id}
            actions={
              note.canRemove
                ? [
                    <Popconfirm
                      key="remove"
                      title={t('web.recruitment.candidates.notes.remove_confirm')}
                      onConfirm={() => removeNote(candidateId, note.id, team).then(() => refresh(candidateId), report)}
                    >
                      <Button type="text" size="small" icon={<DeleteOutlined />} aria-label={t('web.common.delete')} />
                    </Popconfirm>,
                  ]
                : []
            }
          >
            <Flex vertical gap={4}>
              <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginBottom: 0 }}>
                {note.body}
              </Typography.Paragraph>
              <Space size={[4, 4]} wrap>
                <Typography.Text type="secondary">
                  {note.authorName ?? '—'} · {formatDateTime(note.at, i18n.language)}
                </Typography.Text>
                {note.mentioned.map((user) => (
                  <Tag key={user.userId}>@{user.name ?? user.userId}</Tag>
                ))}
              </Space>
            </Flex>
          </List.Item>
        )}
      />
    </Flex>
  );
};
