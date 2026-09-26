import { Link } from 'react-router-dom';
import { receiptsPath } from '../routes';

interface NotFoundPageProps {
  title?: string;
  description?: string;
}

export function NotFoundPage({
  title = 'Не найдено',
  description = 'Страница или запрошенный объект не найден.',
}: NotFoundPageProps) {
  return (
    <div className="layout">
      <header>
        <h1>{title}</h1>
      </header>
      <div className="empty-state">
        <p>{description}</p>
        <Link to={receiptsPath()} className="merchant-link">
          Вернуться к списку чеков
        </Link>
      </div>
    </div>
  );
}
