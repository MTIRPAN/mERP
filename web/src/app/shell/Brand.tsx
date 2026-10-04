import { Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

/** Brand mark: the "m" monogram on the accent gradient (same artwork as the favicon). */
const BrandMark = () => (
  <svg
    width="30"
    height="30"
    viewBox="0 0 32 32"
    aria-hidden
    style={{ flex: 'none', filter: 'drop-shadow(0 4px 10px rgba(99,102,241,0.35))' }}
  >
    <defs>
      <linearGradient id="merp-brand" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stopColor="#6366F1" />
        <stop offset="0.55" stopColor="#8B5CF6" />
        <stop offset="1" stopColor="#06B6D4" />
      </linearGradient>
    </defs>
    <rect width="32" height="32" rx="8" fill="url(#merp-brand)" />
    <path
      d="M8 22V10l5 7 5-7v12"
      fill="none"
      stroke="#fff"
      strokeWidth="2.4"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
    <circle cx="23.5" cy="20.5" r="2" fill="#fff" />
  </svg>
);

export const Brand = ({ collapsed }: { collapsed: boolean }) => {
  const { t } = useTranslation();
  return (
    <Flex align="center" gap={10} style={{ height: 56, paddingInline: 21 }}>
      <BrandMark />
      {!collapsed && (
        <Typography.Text strong style={{ fontSize: 17, letterSpacing: -0.3 }}>
          {t('web.app.name')}
        </Typography.Text>
      )}
    </Flex>
  );
};
