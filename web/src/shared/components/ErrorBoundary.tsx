import { Button, Result } from 'antd';
import { Component, type ErrorInfo, type ReactNode } from 'react';
import { withTranslation, type WithTranslation } from 'react-i18next';

interface State {
  error: Error | undefined;
}

/** Last line of defence for rendering errors; offers a reload instead of a blank page. */
class ErrorBoundaryInner extends Component<WithTranslation & { children: ReactNode }, State> {
  override state: State = { error: undefined };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled rendering error', error, info.componentStack);
  }

  override render() {
    const { t, children } = this.props;
    if (!this.state.error) {
      return children;
    }

    return (
      <Result
        status="500"
        title={t('web.errors.unexpected.title')}
        subTitle={t('web.errors.unexpected.description')}
        extra={
          <Button type="primary" onClick={() => window.location.reload()}>
            {t('web.common.retry')}
          </Button>
        }
      />
    );
  }
}

export const ErrorBoundary = withTranslation()(ErrorBoundaryInner);
