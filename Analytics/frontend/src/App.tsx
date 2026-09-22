import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { PageSizeProvider } from './contexts/PageSizeContext';
import { ToastProvider } from './components/Toasts';
import { Layout } from './components/Layout';
import { ReceiptsPage } from './components/ReceiptsPage';
import { CommoditiesPage } from './components/CommoditiesPage';
import { MerchantsPage } from './components/MerchantsPage';
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
              <Route path="/" element={<ReceiptsPage />} />
              <Route path="/commodities" element={<CommoditiesPage />} />
              <Route path="/merchants" element={<MerchantsPage />} />
            </Route>
          </Routes>
        </PageSizeProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}

export default App;
