import { Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import type { LeaveLegalWarning } from '@/shared/api/generated/model';
import { versionLabel } from './leaveVersions';

/** Settings below the legal minimum (D41): a warning, the record is saved anyway. */
export const LegalWarnings = ({ warnings }: { warnings: LeaveLegalWarning[] }) => {
  const { t, i18n } = useTranslation();
  if (warnings.length === 0) {
    return null;
  }

  return (
    <Alert
      type="warning"
      showIcon
      title={t('web.hr.leave.legal.title')}
      description={
        <ul style={{ margin: 0, paddingInlineStart: 20 }}>
          {warnings.map((w, i) => (
            <li key={`${w.code}-${i}`}>
              {w.validFrom ? `${versionLabel(w.validFrom, t, i18n.language)}: ` : ''}
              {w.message}
            </li>
          ))}
        </ul>
      }
    />
  );
};
