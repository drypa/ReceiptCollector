import { useEffect, useState } from 'react';
import { adminService } from '../services/adminService';

export const useAdmin = () => {
  const [isAdmin, setIsAdmin] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const checkAdminStatus = async () => {
      try {
        setLoading(true);
        const isAdminValue = await adminService.getIsAdmin();
        setIsAdmin(isAdminValue);
      } catch (error) {
        console.error('Ошибка при проверке статуса администратора:', error);
        setIsAdmin(false);
      } finally {
        setLoading(false);
      }
    };

    checkAdminStatus();

    // Опционально: подписка на обновления, если потребуется
  }, []);

  const refresh = async () => {
    setLoading(true);
    await adminService.refresh();
    const isAdminValue = await adminService.getIsAdmin();
    setIsAdmin(isAdminValue);
    setLoading(false);
  };

  return { isAdmin: isAdmin ?? false, loading, refresh };
};