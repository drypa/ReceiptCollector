namespace ReceiptCollector.Analytics.Api.Modules.Users;

public static class UserContext
{
    private static readonly AsyncLocal<Guid?> userId = new();
    public static Guid? UserId => userId.Value;

    public static IDisposable SetUserId(Guid id)
    {
        var rollback = new ContextRollback<Guid?>(userId);
        userId.Value = id;
        return rollback;
    }
    
    private class ContextRollback<T> : IDisposable
    {
        private readonly T originalValue;
        private readonly AsyncLocal<T> valueContainer;

        public ContextRollback(AsyncLocal<T> valueContainer)
        {
            this.valueContainer = valueContainer;
            originalValue = valueContainer.Value;
        }

        public void Dispose()
        {
            valueContainer.Value = originalValue;
        }
    }
}

