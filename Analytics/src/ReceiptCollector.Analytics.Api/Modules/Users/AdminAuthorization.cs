using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Users;

/// <summary>
/// Общая проверка прав администратора для админских эндпоинтов (ADR-028 п. 10):
/// один источник вместо копий в каждом модуле. Поведение идентично прежнему
/// <c>PrecategorizedCommodityEndpoints.AuthorizeAdminAsync</c> (ADR-027 п. 7).
/// </summary>
public static class AdminAuthorization
{
    /// <summary>
    /// Проверка прав по эталону <c>MerchantEndpoints</c> (FR-5.5): нет пользователя → 401,
    /// не админ → 403. Возвращает <c>null</c>, если доступ разрешён.
    /// <para>
    /// 403 отдаётся явно (<see cref="Results.StatusCode(int)"/>): в приложении не зарегистрирована
    /// схема аутентификации, поэтому <c>Results.Forbid()</c> в рантайме бросает
    /// <c>InvalidOperationException</c> и даёт 500 вместо 403 (тесты уровня хендлера этого не видят).
    /// Тот же дефект у <c>MerchantEndpoints</c> — в бэклоге.
    /// </para>
    /// </summary>
    public static async Task<IResult?> AuthorizeAsync(
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userRepository);

        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return null;
    }
}
