namespace ChineseLearningServer;

// Who is calling: the account behind the "Authorization: Bearer <token>" header.
public static class Auth
{
    public static string? BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    public static (long Id, string UserName)? SignedInUser(HttpContext context, Database db) =>
        BearerToken(context) is { Length: > 0 } token ? db.FindToken(token) : null;

    public static IResult Problem(string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Json(new { error = message }, statusCode: status);

    public static IResult Unauthorized() =>
        Problem("Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.", StatusCodes.Status401Unauthorized);
}
