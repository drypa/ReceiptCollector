namespace ReceiptCollector.Analytics.Api.Modules.Users;

public static class UserAuthCookie
{
    public const string CookieName = "rc-auth";

    public const string AuthenticationScheme = "RcAuthCookie";

    public const string HttpContextUserIdKey = "AuthenticatedUserId";
}
