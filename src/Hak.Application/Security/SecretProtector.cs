using System.Security.Cryptography;
using Hak.Data;
using Microsoft.EntityFrameworkCore;

namespace Hak.Application.Security;

/// <summary>
/// Mã hóa bí mật lưu trong CSDL (khóa HMAC trao đổi) bằng AES-256-GCM với khóa chủ nằm NGOÀI CSDL
/// (<c>Hak:SecretKey</c> hoặc tệp secret.key riêng), nên chép/lộ tệp CSDL hay bản sao lưu thì không lộ khóa.
/// Dạng lưu: "enc1:" + base64(nonce 12 | tag 16 | ciphertext). Giá trị cũ chưa mã hóa vẫn đọc được và được nâng cấp lúc khởi động.
/// Chưa cấu hình khóa chủ (ví dụ trong test) → giữ nguyên, không mã hóa.
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "enc1:";
    private static byte[]? masterKey;

    public static bool IsConfigured => masterKey is not null;
    public static bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

    public static void Configure(byte[]? key)
    {
        if (key is { Length: not 32 }) throw new ArgumentException("Khóa chủ phải dài đúng 32 byte (AES-256).", nameof(key));
        masterKey = key;
    }

    /// <summary>Khóa chủ từ cấu hình (base64) hoặc tệp; chưa có thì tạo ngẫu nhiên và lưu vào tệp (chỉ chủ sở hữu đọc được).</summary>
    public static byte[] LoadOrCreateKey(string? configuredBase64, string keyFilePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredBase64)) return Parse(configuredBase64, "Hak:SecretKey");
        if (File.Exists(keyFilePath)) return Parse(File.ReadAllText(keyFilePath), keyFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyFilePath))!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyFilePath, Convert.ToBase64String(key));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return key;
    }

    private static byte[] Parse(string text, string source)
    {
        try
        {
            var key = Convert.FromBase64String(text.Trim());
            if (key.Length == 32) return key;
        }
        catch (FormatException) { }
        throw new InvalidOperationException($"Khóa chủ ở {source} không hợp lệ: cần chuỗi base64 của đúng 32 byte.");
    }

    public static string Protect(string plain) => masterKey is null ? plain : Protect(plain, masterKey);

    public static string Protect(string plain, byte[] masterKey)
    {
        if (IsProtected(plain)) return plain;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = System.Text.Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(masterKey, 16)) aes.Encrypt(nonce, data, cipher, tag);
        return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    /// <summary>Giá trị gốc; giá trị cũ chưa mã hóa trả nguyên.</summary>
    public static string Reveal(string stored)
    {
        if (!IsProtected(stored)) return stored;
        return Reveal(stored, masterKey ?? throw new InvalidOperationException("Dữ liệu đã mã hóa nhưng chưa cấu hình khóa chủ (Hak:SecretKey hoặc tệp secret.key)."));
    }

    public static string Reveal(string stored, byte[] masterKey)
    {
        if (!IsProtected(stored)) return stored;
        var raw = Convert.FromBase64String(stored[Prefix.Length..]);
        if (raw.Length < 28) throw new CryptographicException("Bí mật mã hóa bị hỏng.");
        var plain = new byte[raw.Length - 28];
        try
        {
            using var aes = new AesGcm(masterKey, 16);
            aes.Decrypt(raw.AsSpan(0, 12), raw.AsSpan(28), raw.AsSpan(12, 16), plain);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Không giải mã được bí mật: sai khóa chủ (mất tệp secret.key / Hak:SecretKey?).");
        }
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    /// <summary>Mã hóa các khóa trao đổi còn lưu dạng thường. Trả số khóa đã nâng cấp.</summary>
    public static async Task<int> UpgradeStoredKeysAsync(HakDbContext db)
    {
        if (masterKey is null) return 0;
        var rows = await db.ExchangePartnerKeys.Where(k => !k.SecretBase64.StartsWith(Prefix)).ToListAsync();
        foreach (var k in rows) k.SecretBase64 = Protect(k.SecretBase64);
        if (rows.Count > 0) await db.SaveChangesAsync();
        return rows.Count;
    }
}
