import { DeleteOutlined, PictureOutlined, UploadOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Avatar, Button, Flex, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  getGetCompanyQueryKey,
  useDownloadFile,
  useRemoveCompanyLogo,
  useSetCompanyLogo,
} from '@/shared/api/generated/endpoints/platform/platform';
import { isApiError } from '@/shared/api/problem';
import { useObjectUrl } from '@/shared/files/useObjectUrl';

interface CompanyLogoProps {
  companyId: string;
  logoFileId?: string | null;
}

/** The company's logo with upload and remove (PLAN P7). The picture is fetched with the API client, not an img URL. */
export const CompanyLogo = ({ companyId, logoFileId }: CompanyLogoProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const picture = useDownloadFile(logoFileId ?? '', { query: { enabled: !!logoFileId, staleTime: Infinity } });
  const url = useObjectUrl(picture.data);
  const setLogo = useSetCompanyLogo();
  const removeLogo = useRemoveCompanyLogo();

  const refresh = () => queryClient.invalidateQueries({ queryKey: getGetCompanyQueryKey(companyId) });

  const upload = async (file: File) => {
    try {
      await setLogo.mutateAsync({ id: companyId, data: { file } });
      void message.success(t('web.platform.companies.logo_saved'));
      await refresh();
    } catch (error) {
      const messages = isApiError(error) ? (error.problem?.fieldErrors ?? []).map((e) => e.message) : [];
      if (messages.length > 0) {
        void message.error(messages.join(' '));
      }
    }
  };

  const remove = async () => {
    await removeLogo.mutateAsync({ id: companyId });
    void message.success(t('web.platform.companies.logo_removed'));
    await refresh();
  };

  return (
    <Flex gap={16} align="center">
      <Avatar shape="square" size={72} src={url} icon={<PictureOutlined />} alt={t('web.platform.companies.logo')} />
      <Flex vertical gap={8}>
        <Typography.Text type="secondary">{t('web.platform.companies.logo_hint')}</Typography.Text>
        <Flex gap={8}>
          <Upload
            accept=".png,.jpg,.jpeg,.webp"
            showUploadList={false}
            customRequest={({ file }) => void upload(file as File)}
          >
            <Button icon={<UploadOutlined />} loading={setLogo.isPending}>
              {t('web.platform.companies.logo_upload')}
            </Button>
          </Upload>
          {logoFileId && (
            <Button
              icon={<DeleteOutlined />}
              danger
              loading={removeLogo.isPending}
              onClick={() => void remove().catch(() => undefined)}
            >
              {t('web.platform.companies.logo_remove')}
            </Button>
          )}
        </Flex>
      </Flex>
    </Flex>
  );
};
