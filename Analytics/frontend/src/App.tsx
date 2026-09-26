import { BrowserRouter, Navigate, Route, Routes, useSearchParams } from 'react-router-dom';
import { PageSizeProvider } from './contexts/PageSizeContext';
import { ToastProvider } from './components/Toasts';
import { Layout } from './components/Layout';
import { ReceiptsPage } from './components/ReceiptsPage';
import { ReceiptDetailsPage } from './components/ReceiptDetailsPage';
import { MerchantReceiptsPage } from './components/MerchantReceiptsPage';
import { CommoditiesPage } from './components/CommoditiesPage';
import { MerchantsPage } from './components/MerchantsPage';
import { NotFoundPage } from './components/NotFoundPage';
import { isGuid, receiptPath, receiptsPath } from './routes';
import { adminService } from './services/adminService';
import { useEffect } from 'react';
import './App.css';

export function App() {
  useEffect(() => {
    // Инициализируем сервис администратора при запуске приложения
    adminService.initialize();
  }, []);

  return (
    <BrowserRouter>
      <ToastProvider>
        <PageSizeProvider>
          <Routes>
            <Route element={<Layout />}>
              {/* Совместимость со старыми ссылками: / и /?receiptId={id} (ADR-023, C1). */}
              <Route path="/" element={<RootRedirect />} />
              <Route path="/receipts" element={<ReceiptsPage />} />
              <Route path="/receipts/:receiptId" element={<ReceiptDetailsPage />} />
              <Route path="/commodities" element={<CommoditiesPage />} />
              <Route path="/merchants" element={<MerchantsPage />} />
              <Route path="/merchants/:merchantId/receipts" element={<MerchantReceiptsPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Routes>
        </PageSizeProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}

/**
 * Редирект со старого корня «/» на «/receipts».
 * Старая форма «/?receiptId={id}» (например, переход из раздела «Товары»)
 * превращается в «/receipts/{id}». replace — чтобы редирект не попадал в историю.
 */
function RootRedirect() {
  const [searchParams] = useSearchParams();
  const receiptId = searchParams.get('receiptId');

  if (receiptId) {
    return <Navigate to={isGuid(receiptId) ? receiptPath(receiptId) : receiptsPath()} replace />;
  }

  return <Navigate to={receiptsPath()} replace />;
}

export default App;
