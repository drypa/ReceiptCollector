class AdminService {
  private isAdmin: boolean | null = null;
  private isInitialized = false;

  /**
   * Получает статус администратора с сервера и кэширует его
   */
  async initialize(): Promise<void> {
    if (this.isInitialized) {
      return;
    }

    try {
      const response = await fetch('/api/users/is-admin', {
        method: 'GET',
        credentials: 'include',
      });

      if (response.ok) {
        const data = await response.json();
        this.isAdmin = data.isAdmin;
      } else if (response.status === 401) {
        // Пользователь не авторизован, устанавливаем isAdmin в false
        this.isAdmin = false;
      } else {
        throw new Error(`Ошибка при получении статуса администратора: ${response.status}`);
      }
    } catch (error) {
      console.error('Ошибка при инициализации AdminService:', error);
      this.isAdmin = false; // В случае ошибки считаем, что пользователь не админ
    } finally {
      this.isInitialized = true;
    }
  }

  /**
   * Возвращает статус администратора
   * Если сервис еще не инициализирован, сначала инициализирует его
   */
  async getIsAdmin(): Promise<boolean> {
    if (!this.isInitialized) {
      await this.initialize();
    }
    return this.isAdmin ?? false;
  }

  /**
   * Проверяет, является ли текущий пользователь администратором
   */
  async isAdminUser(): Promise<boolean> {
    return await this.getIsAdmin();
  }

  /**
   * Сбрасывает кэш и повторно инициализирует сервис
   */
  async refresh(): Promise<void> {
    this.isAdmin = null;
    this.isInitialized = false;
    await this.initialize();
  }
}

// Создаем экземпляр сервиса для использования в приложении
export const adminService = new AdminService();

// Экспортируем класс для возможного использования в тестах
export { AdminService };