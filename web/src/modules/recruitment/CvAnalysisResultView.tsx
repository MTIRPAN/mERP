import {
  CheckCircleFilled,
  InfoCircleOutlined,
  MinusCircleFilled,
  QuestionCircleFilled,
  WarningFilled,
} from '@ant-design/icons';
import { Alert, Card, Col, Descriptions, Empty, Flex, Progress, Row, Table, Tabs, Tag, Typography, theme } from 'antd';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type {
  CvAnalysisResponse,
  CvMatch,
  CvRequirementAssessment,
  CvRequirementStatus,
  CvSkill,
} from '@/shared/api/generated/model';
import {
  educationLevelKeys,
  languageLevelKey,
  recommendationColors,
  recommendationKeys,
  redactionKindKeys,
  requirementStatusColors,
  requirementStatusKeys,
  seniorityKeys,
  skillCategoryKeys,
  skillLevelKeys,
} from './cvAnalysisLabels';

const statusIcons: Record<CvRequirementStatus, ReactNode> = {
  Met: <CheckCircleFilled />,
  Partial: <WarningFilled />,
  NotMet: <MinusCircleFilled />,
  Unclear: <QuestionCircleFilled />,
};

const BulletList = ({ items }: { items: string[] }) => (
  <ul style={{ margin: 0, paddingInlineStart: 20 }}>
    {items.map((item) => (
      <li key={item}>{item}</li>
    ))}
  </ul>
);

const MatchCard = ({ match }: { match: CvMatch }) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const color =
    match.score >= 70 ? token.colorSuccess : match.score >= 50 ? token.colorWarning : token.colorTextTertiary;

  return (
    <Card title={t('web.recruitment.cv_analysis.match.title')}>
      <Flex gap={token.marginLG} align="center" wrap>
        <Progress type="dashboard" percent={match.score} strokeColor={color} size={120} />
        <Flex vertical gap={token.marginXS} style={{ flex: 1, minWidth: 240 }}>
          <div>
            <Tag color={recommendationColors[match.recommendation]} variant="filled">
              {t(recommendationKeys[match.recommendation])}
            </Tag>
          </div>
          <Typography.Paragraph style={{ margin: 0 }}>{match.summary}</Typography.Paragraph>
        </Flex>
      </Flex>
      <Table<CvRequirementAssessment>
        style={{ marginTop: token.marginLG }}
        size="small"
        pagination={false}
        rowKey="requirement"
        dataSource={match.requirements}
        columns={[
          {
            key: 'status',
            title: t('web.recruitment.cv_analysis.match.status'),
            width: 150,
            render: (_, row) => (
              <Tag color={requirementStatusColors[row.status]} icon={statusIcons[row.status]}>
                {t(requirementStatusKeys[row.status])}
              </Tag>
            ),
          },
          { key: 'requirement', dataIndex: 'requirement', title: t('web.recruitment.cv_analysis.match.requirement') },
          { key: 'evidence', dataIndex: 'evidence', title: t('web.recruitment.cv_analysis.match.evidence') },
        ]}
      />
      <Row gutter={[token.marginLG, token.margin]} style={{ marginTop: token.marginLG }}>
        <Col xs={24} md={12}>
          <Typography.Text strong>{t('web.recruitment.cv_analysis.match.strengths')}</Typography.Text>
          <BulletList items={match.strengths} />
        </Col>
        <Col xs={24} md={12}>
          <Typography.Text strong>{t('web.recruitment.cv_analysis.match.gaps')}</Typography.Text>
          <BulletList items={match.gaps} />
        </Col>
      </Row>
    </Card>
  );
};

const SkillGroups = ({ skills }: { skills: CvSkill[] }) => {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const groups = [...new Set(skills.map((s) => s.category))].map(
    (category) => [category, skills.filter((s) => s.category === category)] as const,
  );

  return groups.length === 0 ? (
    <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} />
  ) : (
    <Flex vertical gap={token.margin}>
      {groups.map(([category, items]) => (
        <div key={category}>
          <Typography.Text type="secondary">{t(skillCategoryKeys[category])}</Typography.Text>
          <Flex wrap gap={token.marginXS} style={{ marginTop: token.marginXXS }}>
            {items.map((skill) => (
              <Tag key={skill.name} title={skill.evidence} variant="outlined">
                {skill.name}
                {skill.level !== 'Unknown' && (
                  <Typography.Text type="secondary" style={{ marginInlineStart: 6, fontSize: token.fontSizeSM }}>
                    {t(skillLevelKeys[skill.level])}
                  </Typography.Text>
                )}
              </Tag>
            ))}
          </Flex>
        </div>
      ))}
    </Flex>
  );
};

export const CvAnalysisResultView = ({ result }: { result: CvAnalysisResponse }) => {
  const { t, i18n } = useTranslation();
  const { token } = theme.useToken();
  const { analysis } = result;
  const years =
    analysis.totalExperienceYears === null
      ? t('web.recruitment.cv_analysis.unknown')
      : t('web.recruitment.cv_analysis.years', {
          years: new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 }).format(
            analysis.totalExperienceYears,
          ),
        });
  const redactions = result.redactions
    .map((r) => t(redactionKindKeys[r.kind] ?? 'web.recruitment.cv_analysis.redaction.other', { count: r.count }))
    .join(', ');

  return (
    <Flex vertical gap={token.marginLG}>
      <Alert
        type="info"
        showIcon
        icon={<InfoCircleOutlined />}
        title={t('web.recruitment.cv_analysis.notice.title')}
        description={
          <>
            {t('web.recruitment.cv_analysis.notice.description')}
            {redactions && (
              <div style={{ marginTop: token.marginXXS }}>
                {t('web.recruitment.cv_analysis.notice.redactions', { items: redactions })}
              </div>
            )}
            {result.documentTruncated && (
              <div style={{ marginTop: token.marginXXS }}>{t('web.recruitment.cv_analysis.notice.truncated')}</div>
            )}
          </>
        }
      />

      <Card className="merp-gradient-border" variant="borderless">
        <Typography.Title level={4} style={{ marginTop: 0 }}>
          {analysis.currentTitle || t('web.recruitment.cv_analysis.unknown_title')}
        </Typography.Title>
        <Flex gap={token.marginXS} wrap style={{ marginBottom: token.margin }}>
          <Tag variant="filled" color="geekblue">
            {t(seniorityKeys[analysis.seniority])}
          </Tag>
          <Tag variant="filled">{years}</Tag>
        </Flex>
        <Typography.Paragraph style={{ marginBottom: 0 }}>{analysis.summary}</Typography.Paragraph>
      </Card>

      {analysis.match && <MatchCard match={analysis.match} />}

      <Card>
        <Tabs
          items={[
            {
              key: 'experience',
              label: t('web.recruitment.cv_analysis.tabs.experience'),
              children: (
                <Flex vertical gap={token.margin}>
                  {analysis.experiences.map((e) => (
                    <div key={`${e.title}-${e.company}-${e.start}`}>
                      <Typography.Text strong>{e.title}</Typography.Text>
                      <Typography.Text type="secondary">{` · ${e.company}`}</Typography.Text>
                      <div>
                        <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                          {`${e.start || '?'} – ${e.isCurrent ? t('web.recruitment.cv_analysis.present') : e.end || '?'}`}
                        </Typography.Text>
                      </div>
                      <BulletList items={e.highlights} />
                    </div>
                  ))}
                </Flex>
              ),
            },
            {
              key: 'skills',
              label: t('web.recruitment.cv_analysis.tabs.skills'),
              children: <SkillGroups skills={analysis.skills} />,
            },
            {
              key: 'education',
              label: t('web.recruitment.cv_analysis.tabs.education'),
              children: (
                <Flex vertical gap={token.margin}>
                  {analysis.education.map((e) => (
                    <div key={`${e.institution}-${e.field}`}>
                      <Typography.Text strong>{e.institution}</Typography.Text>
                      <div>
                        <Typography.Text>{[e.degree, e.field].filter(Boolean).join(', ')}</Typography.Text>
                        <Typography.Text type="secondary">
                          {` · ${t(educationLevelKeys[e.level])}${e.isOngoing ? ` · ${t('web.recruitment.cv_analysis.ongoing')}` : e.graduationYear ? ` · ${e.graduationYear}` : ''}`}
                        </Typography.Text>
                      </div>
                    </div>
                  ))}
                  {(analysis.languages.length > 0 || analysis.certifications.length > 0) && (
                    <Descriptions
                      column={1}
                      size="small"
                      items={[
                        {
                          key: 'languages',
                          label: t('web.recruitment.cv_analysis.languages'),
                          children: analysis.languages
                            .map((l) => {
                              const key = languageLevelKey(l.level);
                              return `${l.language} (${key ? t(key) : l.level})`;
                            })
                            .join(', '),
                        },
                        {
                          key: 'certifications',
                          label: t('web.recruitment.cv_analysis.certifications'),
                          children: analysis.certifications
                            .map((c) => [c.name, c.issuer, c.year].filter(Boolean).join(' · '))
                            .join('; '),
                        },
                      ]}
                    />
                  )}
                </Flex>
              ),
            },
            {
              key: 'interview',
              label: t('web.recruitment.cv_analysis.tabs.interview'),
              children: (
                <ol style={{ margin: 0, paddingInlineStart: 20 }}>
                  {analysis.interviewQuestions.map((q) => (
                    <li key={q.question} style={{ marginBottom: token.marginSM }}>
                      <Typography.Text>{q.question}</Typography.Text>
                      <div>
                        <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
                          {q.purpose}
                        </Typography.Text>
                      </div>
                    </li>
                  ))}
                </ol>
              ),
            },
            {
              key: 'clarifications',
              label: t('web.recruitment.cv_analysis.tabs.clarifications'),
              children:
                analysis.clarifications.length > 0 ? (
                  <BulletList items={analysis.clarifications} />
                ) : (
                  <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} />
                ),
            },
          ]}
        />
      </Card>
      <Typography.Text type="secondary" style={{ fontSize: token.fontSizeSM }}>
        {t('web.recruitment.cv_analysis.meta', { model: result.model, version: result.promptVersion })}
      </Typography.Text>
    </Flex>
  );
};
