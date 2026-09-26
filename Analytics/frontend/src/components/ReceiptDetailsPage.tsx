import { useNavigate, useParams } from 'react-router-dom';
import { useReceiptDetails } from '../hooks/useReceiptDetails';
import { isGuid, receiptsPath } from '../routes';
import { NotFoundPage } from './NotFoundPage';
import { ReceiptDetails } from './ReceiptDetails';

export function ReceiptDetailsPage() {
  const { receiptId } = useParams<{ receiptId: string }>();

  if (!isGuid(receiptId)) {
    return (
      <NotFoundPage
        title="Чек не найден"
        description="Ссылка содержит некорректный идентификатор чека."
      />
    );
  }

  return <ReceiptDetailsView key={receiptId} receiptId={receiptId} />;
}

interface ReceiptDetailsViewProps {
  receiptId: string;
}

function ReceiptDetailsView({ receiptId }: ReceiptDetailsViewProps) {
  const navigate = useNavigate();
  const { receipt, isLoading, notFound, error, refresh } = useReceiptDetails(receiptId);

  /**
   * «Назад» возвращает на предыдущий экран, если он был внутри приложения
   * (idx > 0 — признак push-перехода react-router в этом документе),
   * иначе — на общий список чеков (ADR-023, D1).
   */
  const handleBack = () => {
    const idx: number | undefined = window.history.state?.idx ?? undefined;
    if (typeof idx === 'number' && idx > 0) {
      navigate(-1);
      return;
    }

    navigate(receiptsPath());
  };

  if (isLoading) {
    return (
      <div className="layout">
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем детали чека...
        </div>
      </div>
    );
  }

  if (notFound) {
    return (
      <NotFoundPage
        title="Чек не найден"
        description="Чек, указанный в ссылке, не найден."
      />
    );
  }

  if (error) {
    return (
      <div className="layout">
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить чек: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      </div>
    );
  }

  return (
    // Обёртка .layout обязательна: сегодня карточка рендерится внутри <main className="layout">
    // (ReceiptsPage.tsx:167-175), а .receipt-details имеет свои max-width/padding.
    // Без .layout карточка станет шире и с другими отступами (ADR-023, факт 12).
    <div className="layout">
      <ReceiptDetails receipt={receipt} onBack={handleBack} onReceiptRefresh={refresh} />
    </div>
  );
}
