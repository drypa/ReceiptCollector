import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { usePageSize } from '../contexts/PageSizeContext';
import { useReceipts } from '../hooks/useReceipts';
import { isGuid, merchantReceiptsPath, receiptPath, receiptsPath } from '../routes';
import { NotFoundPage } from './NotFoundPage';
import { ReceiptsListView } from './ReceiptsListView';

export function MerchantReceiptsPage() {
  const { merchantId } = useParams<{ merchantId: string }>();

  // Проверка формата — до любых хуков с данными и без запроса (ADR-023, I1).
  if (!isGuid(merchantId)) {
    return (
      <NotFoundPage
        title="Магазин не найден"
        description="Ссылка содержит некорректный идентификатор магазина."
      />
    );
  }

  // key — чтобы смена магазина пересоздавала экран вместе с хуком (ADR-023, G1):
  // react-router не размонтирует компонент при смене параметра в том же маршруте.
  return <MerchantReceiptsView key={merchantId} merchantId={merchantId} />;
}

interface MerchantReceiptsViewProps {
  merchantId: string;
}

function MerchantReceiptsView({ merchantId }: MerchantReceiptsViewProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const { pageSize } = usePageSize();
  const list = useReceipts({ pageSize, merchantId });

  if (list.notFound) {
    return (
      <NotFoundPage
        title="Магазин не найден"
        description="Магазин, указанный в ссылке, не найден."
      />
    );
  }

  const handleViewMerchantReceipts = (targetMerchantId: string) => {
    const path = merchantReceiptsPath(targetMerchantId);
    // На этом же экране не плодим дублирующую запись истории.
    if (path !== location.pathname) {
      navigate(path);
    }
  };

  return (
    <ReceiptsListView
      title="Чеки по магазину"
      list={list}
      headerAction={
        <button
          type="button"
          onClick={() => navigate(receiptsPath())}
          style={{ marginLeft: '1rem' }}
          className="secondary"
        >
          Все чеки
        </button>
      }
      onViewMerchantReceipts={handleViewMerchantReceipts}
      onReceiptClick={(receiptId) => navigate(receiptPath(receiptId))}
    />
  );
}
