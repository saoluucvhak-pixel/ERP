using System.Security.Cryptography;
using Hak.Application.Security;

namespace Hak.Integration.Tests;

public class SecretProtectorTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Round_trip_and_not_plaintext()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var stored = SecretProtector.Protect(secret, Key);
        Assert.StartsWith("enc1:", stored);
        Assert.DoesNotContain(secret, stored);
        Assert.Equal(secret, SecretProtector.Reveal(stored, Key));
        Assert.NotEqual(stored, SecretProtector.Protect(secret, Key));   // nonce ngẫu nhiên
        Assert.Equal(stored, SecretProtector.Protect(stored, Key));      // không mã hóa hai lần
    }

    [Fact]
    public void Legacy_plain_value_still_readable()
        => Assert.Equal("abc=", SecretProtector.Reveal("abc=", Key));

    [Fact]
    public void Wrong_key_or_tampering_is_rejected()
    {
        var stored = SecretProtector.Protect("c2VjcmV0", Key);
        Assert.Throws<InvalidOperationException>(() => SecretProtector.Reveal(stored, RandomNumberGenerator.GetBytes(32)));
        var raw = Convert.FromBase64String(stored[5..]);
        raw[^1] ^= 1;
        Assert.Throws<InvalidOperationException>(() => SecretProtector.Reveal("enc1:" + Convert.ToBase64String(raw), Key));
    }

    [Fact]
    public void Key_file_is_created_once_and_reused()
    {
        var path = Path.Combine(Path.GetTempPath(), "hak-sk-" + Guid.NewGuid().ToString("N"), "secret.key");
        try
        {
            var a = SecretProtector.LoadOrCreateKey(null, path);
            Assert.Equal(32, a.Length);
            Assert.Equal(a, SecretProtector.LoadOrCreateKey(null, path));
            Assert.Equal(Key, SecretProtector.LoadOrCreateKey(Convert.ToBase64String(Key), path));   // cấu hình ưu tiên hơn tệp
            Assert.Throws<InvalidOperationException>(() => SecretProtector.LoadOrCreateKey("không-phải-base64", path));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
