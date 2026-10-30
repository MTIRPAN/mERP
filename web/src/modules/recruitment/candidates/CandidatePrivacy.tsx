import { DownloadOutlined } from '@ant-design/icons';
import { App, Button, List, Popconfirm, Space, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { eraseCandidate, exportCandidateData } from '@/shared/api/generated/endpoints/recruitment/recruitment';
import type { CandidateConsentItem, CandidateConsentKind, CandidateDetails } from '@/shared/api/generated/model';
import { saveFile } from '@/shared/api/download';
import { isApiError } from '@/shared/api/problem';
import { formatDateTime } from '@/shared/format/date';
import { useCandidateRefresh } from './candidates';

const kindColors: Record<CandidateConsentKind, string> = {
  Disclosure: 'default',
  Pool: 'blue',
  Renewed: 'green',
  Withdrawn: 'volcano',
};

/** Data subject requests on a candidate (PLAN R8, D55): the data as a ZIP, and the erasure on request. */
export const PrivacyActions = ({ candidate }: { candidate: CandidateDetails }) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const refresh = useCandidateRefresh();

  const download = () =>
    void saveFile(
      exportCandidateData(candidate.id),
      t('web.recruitment.candidates.privacy.export_file', { code: candidate.id.replaceAll('-', '').slice(0, 8) }),
    ).catch(() => undefined);

  const erase = async () => {
    try {
      await eraseCandidate(candidate.id, { version: candidate.version });
      void message.success(t('web.recruitment.candidates.privacy.erased'));
      await refresh(candidate.id);
    } catch (error) {
      void message.error(isApiError(error) ? error.message : String(error));
    }
  };

  return (
    <Space wrap>
      <Button icon={<DownloadOutlined />} onClick={download}>
        {t('web.recruitment.candidates.privacy.export')}
      </Button>
      <Popconfirm
        title={t('web.recruitment.candidates.privacy.erase_confirm')}
        description={t('web.recruitment.candidates.privacy.erase_hint')}
        okButtonProps={{ danger: true }}
        onConfirm={erase}
      >
        <Button danger>{t('web.recruitment.candidates.privacy.erase')}</Button>
      </Popconfirm>
    </Space>
  );
};

/** What the candidate consented to: the texts accepted on the forms and the later renewals and withdrawals (D55). */
export const CandidateConsents = ({ consents }: { consents: CandidateConsentItem[] }) => {
  const { t, i18n } = useTranslation();
  return (
    <List
      dataSource={consents}
      locale={{ emptyText: t('web.recruitment.candidates.privacy.no_consents') }}
      renderItem={(item) => (
        <List.Item>
          <Space wrap>
            <Tag color={kindColors[item.kind]}>{t(`web.recruitment.candidates.privacy.kinds.${item.kind}`)}</Tag>
            {item.revision != null && (
              <Typography.Text>
                {t('web.recruitment.candidates.privacy.revision', { revision: item.revision })}
              </Typography.Text>
            )}
            {item.openingTitle && <Typography.Text type="secondary">{item.openingTitle}</Typography.Text>}
            <Typography.Text type="secondary">{formatDateTime(item.at, i18n.language)}</Typography.Text>
          </Space>
        </List.Item>
      )}
    />
  );
};
