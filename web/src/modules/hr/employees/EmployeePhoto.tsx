import { DeleteOutlined, UploadOutlined, UserOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { App, Avatar, Button, Flex, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  getGetEmployeePhotoQueryKey,
  getGetEmployeeQueryKey,
  getListEmployeesQueryKey,
  useGetEmployeePhoto,
  useRemoveEmployeePhoto,
  useSetEmployeePhoto,
} from '@/shared/api/generated/endpoints/hr/hr';
import { isApiError } from '@/shared/api/problem';
import { useObjectUrl } from '@/shared/files/useObjectUrl';

interface EmployeeAvatarProps {
  employeeId: string;
  /** The current photo; none shows the placeholder without a request. */
  photoFileId?: string | null;
  size?: number;
}

/**
 * A person's photo (D38). It is fetched with the API client, not an img URL; the file id is part of the key, so a new
 * photo is fetched once and the old one is never shown again.
 */
export const EmployeeAvatar = ({ employeeId, photoFileId, size = 32 }: EmployeeAvatarProps) => {
  const picture = useGetEmployeePhoto(employeeId, {
    query: {
      queryKey: [...getGetEmployeePhotoQueryKey(employeeId), photoFileId],
      enabled: !!photoFileId,
      staleTime: Infinity,
    },
  });
  const url = useObjectUrl(picture.data);
  return <Avatar size={size} src={photoFileId ? url : undefined} icon={<UserOutlined />} />;
};

interface EmployeePhotoProps {
  employeeId: string;
  photoFileId?: string | null;
  canManage: boolean;
}

/** The photo on the employee card, with upload and remove for those who may change the card. */
export const EmployeePhoto = ({ employeeId, photoFileId, canManage }: EmployeePhotoProps) => {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const queryClient = useQueryClient();
  const setPhoto = useSetEmployeePhoto();
  const removePhoto = useRemoveEmployeePhoto();

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: getGetEmployeeQueryKey(employeeId) }),
      queryClient.invalidateQueries({ queryKey: getListEmployeesQueryKey() }),
    ]);

  const upload = async (file: File) => {
    try {
      await setPhoto.mutateAsync({ id: employeeId, data: { file } });
      void message.success(t('web.hr.employees.photo.saved'));
      await refresh();
    } catch (error) {
      const messages = isApiError(error) ? (error.problem?.fieldErrors ?? []).map((e) => e.message) : [];
      if (messages.length > 0) {
        void message.error(messages.join(' '));
      }
    }
  };

  const remove = async () => {
    await removePhoto.mutateAsync({ id: employeeId });
    void message.success(t('web.hr.employees.photo.removed'));
    await refresh();
  };

  return (
    <Flex vertical gap={8} align="center">
      <EmployeeAvatar employeeId={employeeId} photoFileId={photoFileId} size={96} />
      {canManage && (
        <Flex gap={4}>
          <Upload
            accept=".png,.jpg,.jpeg,.webp"
            showUploadList={false}
            customRequest={({ file }) => void upload(file as File)}
          >
            <Button
              size="small"
              type="text"
              icon={<UploadOutlined />}
              loading={setPhoto.isPending}
              aria-label={t('web.hr.employees.photo.upload')}
              title={t('web.hr.employees.photo.upload')}
            />
          </Upload>
          {photoFileId && (
            <Button
              size="small"
              type="text"
              danger
              icon={<DeleteOutlined />}
              loading={removePhoto.isPending}
              aria-label={t('web.hr.employees.photo.remove')}
              title={t('web.hr.employees.photo.remove')}
              onClick={() => void remove().catch(() => undefined)}
            />
          )}
        </Flex>
      )}
    </Flex>
  );
};
