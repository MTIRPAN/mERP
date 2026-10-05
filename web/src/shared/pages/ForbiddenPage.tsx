import { ResultPage } from './ResultPage';

export const ForbiddenPage = () => (
  <ResultPage status="403" titleKey="web.errors.forbidden.title" descriptionKey="web.errors.forbidden.description" />
);
