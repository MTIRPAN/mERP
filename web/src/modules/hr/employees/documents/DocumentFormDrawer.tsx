import { UploadOutlined } from '@ant-design/icons';
import { Button, Col, DatePicker, Form, Input, Row, Select, Typography, Upload, type UploadFile } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  getListEmployeeDocumentsQueryKey,
  useAddEmployeeDocument,
  useListEmployeeDocumentTypes,
  useUpdateEmployeeDocument,
} from '@/shared/api/generated/endpoints/hr/hr';
import type { EmployeeDocumentItem } from '@/shared/api/generated/model';
import { FormDrawer } from '@/shared/components/FormDrawer';
import { formatFileSize } from '@/shared/files/fileSize';
import { useQueryClient } from '@tanstack/react-query';
import { day, text } from '../forms/formValues';

interface DocumentFormValues {
  typeId?: string;
  number?: string;
  issuedOn?: Dayjs | null;
  validUntil?: Dayjs | null;
  note?: string;
  file?: UploadFile[];
}

interface DocumentFormDrawerProps {
  employeeId: string;
  /** Document to edit; undefined adds one with its file. */
  document?: EmployeeDocumentItem;
  /** Accepted extensions and size, from the document list. */
  extensions: string[];
  maxBytes: number;
  open: boolean;
  onClose: () => void;
}

/** Adds a document with its file, or changes a document's type, number, validity and note (D38). */
export const DocumentFormDrawer = ({
  employeeId,
  document,
  extensions,
  maxBytes,
  open,
  onClose,
}: DocumentFormDrawerProps) => {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const add = useAddEmployeeDocument();
  const update = useUpdateEmployeeDocument();
  const types = useListEmployeeDocumentTypes(undefined, { query: { enabled: open } });
  const required = [{ required: true, message: t('web.platform.fields.required') }];
  const editing = !!document;

  const submit = async (values: DocumentFormValues) => {
    if (document) {
      await update.mutateAsync({
        employeeId,
        documentId: document.id,
        data: {
          version: document.version,
          typeId: values.typeId ?? null,
          number: text(values.number),
          issuedOn: day(values.issuedOn),
          validUntil: day(values.validUntil),
          note: text(values.note),
        },
      });
    } else {
      const file = values.file?.[0]?.originFileObj;
      await add.mutateAsync({
        employeeId,
        data: {
          typeId: values.typeId,
          number: text(values.number) ?? undefined,
          issuedOn: day(values.issuedOn) ?? undefined,
          validUntil: day(values.validUntil) ?? undefined,
          note: text(values.note) ?? undefined,
          file,
        },
      });
    }

    await queryClient.invalidateQueries({ queryKey: getListEmployeeDocumentsQueryKey(employeeId) });
  };

  // The current type stays selectable even when it has been retired
  const typeOptions = [
    ...(document && !types.data?.some((type) => type.id === document.typeId)
      ? [{ value: document.typeId, label: document.typeName }]
      : []),
    ...(types.data ?? []).map((type) => ({ value: type.id, label: type.name })),
  ];

  return (
    <FormDrawer<DocumentFormValues>
      key={document ? `${document.id}-${document.version}` : 'new'}
      title={t(editing ? 'web.hr.documents.edit' : 'web.hr.documents.add')}
      open={open}
      onClose={onClose}
      onSubmit={submit}
      fieldAliases={{ file: ['file'] }}
      initialValues={
        document
          ? {
              typeId: document.typeId,
              number: document.number ?? undefined,
              issuedOn: document.issuedOn ? dayjs(document.issuedOn) : null,
              validUntil: document.validUntil ? dayjs(document.validUntil) : null,
              note: document.note ?? undefined,
            }
          : {}
      }
    >
      <Form.Item name="typeId" label={t('web.hr.documents.fields.type')} rules={required}>
        <Select showSearch={{ optionFilterProp: 'label' }} loading={types.isLoading} options={typeOptions} />
      </Form.Item>
      <Form.Item name="number" label={t('web.hr.documents.fields.number')}>
        <Input maxLength={50} />
      </Form.Item>
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="issuedOn" label={t('web.hr.documents.fields.issued_on')}>
            <DatePicker style={{ width: '100%' }} format="L" />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item
            name="validUntil"
            label={t('web.hr.documents.fields.valid_until')}
            extra={t('web.hr.documents.valid_until_hint')}
          >
            <DatePicker style={{ width: '100%' }} format="L" />
          </Form.Item>
        </Col>
      </Row>
      <Form.Item name="note" label={t('web.hr.documents.fields.note')}>
        <Input.TextArea rows={2} maxLength={500} />
      </Form.Item>
      {!editing && (
        <Form.Item
          name="file"
          label={t('web.hr.documents.fields.file')}
          valuePropName="fileList"
          getValueFromEvent={(event: { fileList: UploadFile[] }) => event.fileList.slice(-1)}
          rules={required}
          extra={
            <Typography.Text type="secondary">
              {t('web.hr.documents.file_hint', {
                extensions: extensions.join(', '),
                size: formatFileSize(maxBytes, i18n.language),
              })}
            </Typography.Text>
          }
        >
          <Upload beforeUpload={() => false} maxCount={1} accept={extensions.map((e) => `.${e}`).join(',')}>
            <Button icon={<UploadOutlined />}>{t('web.hr.documents.choose_file')}</Button>
          </Upload>
        </Form.Item>
      )}
    </FormDrawer>
  );
};
