import { useNavigate } from 'react-router-dom';
import { usePageSize } from '../contexts/PageSizeContext';
import { useReceipts } from '../hooks/useReceipts';
import { merchantReceiptsPath, receiptPath } from '../routes';
import { ReceiptsListView } from './ReceiptsListView';

export function ReceiptsPage() {
  const navigate = useNavigate();
  const { pageSize } = usePageSize();
  const list = useReceipts({ pageSize });

  return (
    <ReceiptsListView
      title="Мои чеки"
      list={list}
      onViewMerchantReceipts={(merchantId) => navigate(merchantReceiptsPath(merchantId))}
      onReceiptClick={(receiptId) => navigate(receiptPath(receiptId))}
    />
  );
}
