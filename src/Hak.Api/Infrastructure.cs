using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hak.Application.Security;
using Hak.Domain.Common;

namespace Hak.Api;

/// <summary>
/// Money crosses the API as decimal strings so JavaScript never turns it into a binary double
/// (master prompt §28). Reading accepts strings only.
/// </summary>
public sealed class DecimalAsStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Số tiền phải truyền dạng chuỗi thập phân, ví dụ \"1250000\".");
        // Đầu vào sai (chữ, rỗng, tràn decimal, dấu phân cách lạ) là lỗi của người gọi: JsonException → 400, không để FormatException/OverflowException thành 500.
        return decimal.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonException("Số tiền không hợp lệ: cần chuỗi thập phân dùng dấu chấm, ví dụ \"1250000.5\".");
    }

    /// <summary>
    /// Canonical form without trailing fraction zeros ("1250000", "0", "25133.333333"): PostgreSQL numeric(19,4) comes back as
    /// 0.0000, SQLite storage as 0; clients compare and display the same string either way.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteStringValue(Canonical(value));

    public static string Canonical(decimal value)
    {
        var s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s == "-0" ? "0" : s;
    }
}

/// <summary>Maps domain errors to HTTP status + stable error code body.</summary>
public sealed class DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (DomainException ex)
        {
            ctx.Response.StatusCode = ex.Code switch
            {
                ErrorCodes.Unauthenticated => StatusCodes.Status401Unauthorized,
                ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
                ErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ErrorCodes.ConcurrencyConflict or ErrorCodes.AlreadyPosted or ErrorCodes.DuplicateSource
                    or ErrorCodes.PackageReplayed or ErrorCodes.DuplicateCode => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status422UnprocessableEntity,
            };
            await ctx.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message, errors = ex.Errors });
        }
        catch (BadHttpRequestException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { code = ErrorCodes.ValidationFailed, message = ex.InnerException?.Message ?? ex.Message });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled error");
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "Lỗi hệ thống; dữ liệu không bị thay đổi nếu thao tác chưa hoàn tất." });
        }
    }
}

public static class HttpContextExtensions
{
    /// <summary>Bearer token → user; X-Company-Id → company-scoped permissions. Resolved on every request.</summary>
    public static async Task<SessionContext> SessionAsync(this HttpContext http)
    {
        var (user, session) = await AuthAsync(http);
        if (!Guid.TryParse(http.Request.Headers["X-Company-Id"], out var companyId))
            throw new DomainException(ErrorCodes.Forbidden, "Thiếu header X-Company-Id.");
        return await http.RequestServices.GetRequiredService<AuthService>()
            .ResolveContextAsync(user.Id, companyId, session.Id, http.Request.Headers["X-Device"].FirstOrDefault());
    }

    public static async Task<(Hak.Domain.Security.User User, Hak.Domain.Security.LoginSession Session)> AuthAsync(this HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
            throw new DomainException(ErrorCodes.Unauthenticated, "Chưa đăng nhập.");
        return await http.RequestServices.GetRequiredService<AuthService>().AuthenticateAsync(header["Bearer ".Length..]);
    }
}
