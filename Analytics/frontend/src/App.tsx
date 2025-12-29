import './App.css';
import { ReceiptsPage } from './components/ReceiptsPage';
import { adminService } from './services/adminService';
import { useEffect } from 'react';

export function App() {
  useEffect(() => {
    // Инициализируем сервис администратора при запуске приложения
    adminService.initialize();
  }, []);

  return <ReceiptsPage />;
}

export default App;
