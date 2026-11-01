import { ArrowLeftOutlined, DeleteOutlined, FilePdfOutlined, RollbackOutlined, SaveOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Collapse,
  Flex,
  Popconfirm,
  Row,
  Skeleton,
  Space,
  Switch,
  Tabs,
  Typography,
  theme,
} from 'antd';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useMe } from '@/app/me/meContext';
import { platformAiFeatures, platformPermissions } from '@/modules/platform/permissions';
import { useAiFeature } from '@/shared/ai/useAiFeature';
import {
  getGetDocumentTemplateQueryKey,
  getListDocumentTemplatesQueryKey,
  useCreateDocumentTemplate,
  useDeleteDocumentTemplate,
  useGetDocumentTemplate,
  useListDocumentTypes,
  usePreviewDocument,
  usePreviewDocumentPdf,
  useUpdateDocumentTemplate,
} from '@/shared/api/generated/endpoints/platform/platform';
import { useHasPermission } from '@/shared/auth/permissionContext';
import { CodeEditor } from '@/shared/components/code-editor/CodeEditor';
import { AiBadge } from '@/shared/components/AiBadge';
import { PageHeader } from '@/shared/components/PageHeader';
import { TemplateDraftModal } from './TemplateDraftModal';
import { otherFieldErrors, templateError, type TemplateError } from './templateErrors';

interface TemplateContent {
  body: string;
  css: string;
}

/** Waits this long after the last key stroke before rendering the preview. */
const PREVIEW_DELAY_MS = 600;

/**
 * Editor of a document template (PLAN P12b, D36): Liquid HTML and CSS in Monaco, a live preview of the type's
 * sample model in a sandboxed frame, and a PDF preview through the same converter real documents use. A new template
 * starts from the type's built-in one; its type, company and culture come from the address.
 */
export const DocumentTemplateEditorPage = () => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const { message } = App.useApp();
  const me = useMe();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { id } = useParams<{ id: string }>();
  const [search] = useSearchParams();
  const canManage = useHasPermission(platformPermissions.documentTemplatesManage);

  const template = useGetDocumentTemplate(id ?? '', { query: { enabled: !!id } });
  const types = useListDocumentTypes();
  const create = useCreateDocumentTemplate();
  const update = useUpdateDocumentTemplate();
  const remove = useDeleteDocumentTemplate();
  const { mutateAsync: renderPreview } = usePreviewDocument();
  const pdf = usePreviewDocumentPdf();

  const typeKey = template.data?.documentType ?? search.get('type');
  const type = types.data?.find((item) => item.key === typeKey);
  const companyId = id ? (template.data?.companyId ?? null) : search.get('company');
  const culture = template.data?.culture ?? search.get('culture') ?? me.cultures[0]?.code ?? '';

  const [edited, setEdited] = useState<TemplateContent>();
  const [active, setActive] = useState<boolean>();
  const [tab, setTab] = useState('body');
  const [html, setHtml] = useState<string>();
  const [error, setError] = useState<TemplateError>();
  const [problems, setProblems] = useState<string[]>([]);
  const [drafting, setDrafting] = useState(false);
  const [draft, setDraft] = useState<{ notes: string[]; valid: boolean }>();
  const draftAi = useAiFeature(platformAiFeatures.documentTemplateDraft);

  const saved = template.data ? { body: template.data.body, css: template.data.css ?? '' } : undefined;
  const builtIn = type ? { body: type.defaultBody, css: type.defaultCss } : undefined;
  const content = edited ?? saved ?? (id ? undefined : builtIn);
  const isActive = active ?? template.data?.isActive ?? true;
  const body = content?.body;
  const css = content?.css;

  // Live preview: the server renders the sample model with the content being edited
  useEffect(() => {
    if (body === undefined || !type) {
      return;
    }

    let cancelled = false;
    const timer = setTimeout(() => {
      renderPreview({ data: { documentType: type.key, companyId, culture, body, css: css || null } })
        .then((result) => {
          if (!cancelled) {
            setHtml(result.html);
            setError(undefined);
            setProblems([]);
          }
        })
        .catch((reason: unknown) => {
          if (!cancelled) {
            setError(templateError(reason));
            setProblems(otherFieldErrors(reason));
          }
        });
    }, PREVIEW_DELAY_MS);

    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [body, css, type, companyId, culture, renderPreview]);

  if (!types.data || (id && !template.data) || !content || !type) {
    return types.data && !type && !id ? (
      <Alert type="error" title={t('web.document_templates.unknown_type')} />
    ) : (
      <Skeleton active />
    );
  }

  const change = (part: Partial<TemplateContent>) => setEdited({ ...content, ...part });
  const companyName = id
    ? (template.data?.companyName ?? t('web.document_templates.all_companies'))
    : (me.companies.find((c) => c.id === companyId)?.name ?? t('web.document_templates.all_companies'));
  const cultureName = me.cultures.find((c) => c.code === culture)?.name ?? culture;

  const save = async () => {
    try {
      if (template.data) {
        await update.mutateAsync({
          id: template.data.id,
          data: { version: template.data.version, body: content.body, css: content.css || null, isActive },
        });
        await queryClient.invalidateQueries({ queryKey: getGetDocumentTemplateQueryKey(template.data.id) });
        await queryClient.invalidateQueries({ queryKey: getListDocumentTemplatesQueryKey() });
        setEdited(undefined);
        setActive(undefined);
        void message.success(t('web.document_templates.saved'));
      } else {
        const created = await create.mutateAsync({
          data: { documentType: type.key, companyId, culture, body: content.body, css: content.css || null },
        });
        await queryClient.invalidateQueries({ queryKey: getListDocumentTemplatesQueryKey() });
        void message.success(t('web.document_templates.saved'));
        void navigate(`/system/document-templates/${created.id}`, { replace: true });
      }
    } catch (reason) {
      const bodyError = templateError(reason);
      if (bodyError) {
        setError(bodyError);
        setTab('body');
      }

      const others = otherFieldErrors(reason);
      if (others.length) {
        void message.error(others.join(' '));
      }
    }
  };

  const openPdf = async () => {
    const file: unknown = await pdf.mutateAsync({
      data: { documentType: type.key, companyId, culture, body: content.body, css: content.css || null },
    });
    if (file instanceof Blob) {
      const url = URL.createObjectURL(file);
      window.open(url, '_blank');
      // The new tab reads the file first
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    }
  };

  const deleteTemplate = async () => {
    await remove.mutateAsync({ id: template.data!.id });
    await queryClient.invalidateQueries({ queryKey: getListDocumentTemplatesQueryKey() });
    void navigate('/system/document-templates');
  };

  const editor = (language: 'html' | 'css') => (
    <CodeEditor
      language={language}
      label={t(language === 'html' ? 'web.document_templates.body' : 'web.document_templates.css')}
      value={language === 'html' ? content.body : content.css}
      onChange={(value) => change(language === 'html' ? { body: value } : { css: value })}
      readOnly={!canManage}
      height={520}
      error={language === 'html' ? error : undefined}
    />
  );

  return (
    <>
      <PageHeader
        title={type.name}
        subtitle={[
          companyName,
          cultureName,
          template.data
            ? t('web.document_templates.revision_value', { revision: template.data.revision })
            : t('web.document_templates.not_saved'),
        ].join(' · ')}
        extra={
          <Space wrap>
            <Button icon={<ArrowLeftOutlined />} onClick={() => void navigate('/system/document-templates')}>
              {t('web.document_templates.back')}
            </Button>
            {canManage && draftAi && (
              <Button onClick={() => setDrafting(true)}>
                <Space size={4}>
                  {t('web.document_templates.draft.button')}
                  <AiBadge />
                </Space>
              </Button>
            )}
            <Button icon={<FilePdfOutlined />} loading={pdf.isPending} onClick={() => void openPdf()}>
              {t('web.document_templates.preview_pdf')}
            </Button>
            {canManage && template.data && (
              <Popconfirm title={t('web.document_templates.delete_confirm')} onConfirm={() => deleteTemplate()}>
                <Button danger icon={<DeleteOutlined />}>
                  {t('web.common.delete')}
                </Button>
              </Popconfirm>
            )}
            {canManage && (
              <Button
                type="primary"
                icon={<SaveOutlined />}
                loading={create.isPending || update.isPending}
                onClick={() => void save()}
              >
                {t('web.common.save')}
              </Button>
            )}
          </Space>
        }
      />
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={13}>
          <Card
            size="small"
            extra={
              canManage && (
                <Space>
                  {template.data && (
                    <Space size={4}>
                      <Switch
                        size="small"
                        checked={isActive}
                        onChange={setActive}
                        aria-label={t('web.platform.fields.active')}
                      />
                      <Typography.Text>{t('web.platform.fields.active')}</Typography.Text>
                    </Space>
                  )}
                  <Popconfirm title={t('web.document_templates.reset_confirm')} onConfirm={() => setEdited(builtIn)}>
                    <Button size="small" icon={<RollbackOutlined />}>
                      {t('web.document_templates.reset')}
                    </Button>
                  </Popconfirm>
                </Space>
              )
            }
          >
            <Tabs
              activeKey={tab}
              onChange={setTab}
              items={[
                { key: 'body', label: t('web.document_templates.body'), children: editor('html') },
                { key: 'css', label: t('web.document_templates.css'), children: editor('css') },
              ]}
            />
            {draft && (
              <Alert
                type={draft.valid ? 'info' : 'warning'}
                showIcon
                closable
                onClose={() => setDraft(undefined)}
                title={t(draft.valid ? 'web.document_templates.draft.notice' : 'web.document_templates.draft.invalid')}
                description={
                  draft.notes.length > 0 && (
                    <ul style={{ margin: 0, paddingInlineStart: 20 }}>
                      {draft.notes.map((note) => (
                        <li key={note}>{note}</li>
                      ))}
                    </ul>
                  )
                }
                style={{ marginTop: 12 }}
              />
            )}
            {error && <Alert type="error" showIcon title={error.message} style={{ marginTop: 12 }} />}
            {problems.map((problem) => (
              <Alert key={problem} type="error" showIcon title={problem} style={{ marginTop: 12 }} />
            ))}
          </Card>
          <Collapse
            style={{ marginTop: 16 }}
            items={[
              {
                key: 'fields',
                label: t('web.document_templates.fields'),
                children: (
                  <Flex vertical gap={6}>
                    {type.fields.map((field) => (
                      <div key={field.path}>
                        <Typography.Text
                          code
                          copyable={{ text: `{{ ${field.path} }}` }}
                        >{`{{ ${field.path} }}`}</Typography.Text>{' '}
                        <Typography.Text type="secondary">{field.description}</Typography.Text>
                      </div>
                    ))}
                    {type.partials.map((partial) => (
                      <div key={partial.path}>
                        <Typography.Text code copyable={{ text: `{% render '${partial.path}' %}` }}>
                          {`{% render '${partial.path}' %}`}
                        </Typography.Text>{' '}
                        <Typography.Text type="secondary">{partial.description}</Typography.Text>
                      </div>
                    ))}
                    <Typography.Text type="secondary">{t('web.document_templates.texts_hint')}</Typography.Text>
                  </Flex>
                ),
              },
              {
                key: 'sample',
                label: t('web.document_templates.sample'),
                children: (
                  <pre style={{ margin: 0, maxHeight: 320, overflow: 'auto', fontSize: 12 }}>
                    {JSON.stringify(type.sampleModel, null, 2)}
                  </pre>
                ),
              },
            ]}
          />
        </Col>
        <Col xs={24} xl={11}>
          <Card size="small" title={t('web.document_templates.preview')}>
            {html ? (
              // No scripts, forms or navigation: the page only shows what the template renders
              <iframe
                title={t('web.document_templates.preview')}
                sandbox=""
                srcDoc={html}
                style={{
                  width: '100%',
                  height: 720,
                  border: `1px solid ${token.colorBorderSecondary}`,
                  background: '#ffffff',
                }}
              />
            ) : (
              <Skeleton active />
            )}
          </Card>
        </Col>
      </Row>
      {drafting && (
        <TemplateDraftModal
          documentType={type.key}
          culture={culture}
          current={content}
          onDraft={(result) => {
            change({ body: result.body, css: result.css });
            setDraft({ notes: result.notes, valid: result.valid });
            setTab('body');
          }}
          onClose={() => setDrafting(false)}
        />
      )}
    </>
  );
};
