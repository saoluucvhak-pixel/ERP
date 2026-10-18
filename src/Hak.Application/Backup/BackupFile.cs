using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hak.Domain.Common;

namespace Hak.Application.Backup;

/// <summary>Clear-text header of a backup file. Authenticated: its hash is part of every chunk's associated data.</summary>
public sealed record BackupHeader(int Version, DateTime CreatedAt, string SourceProvider, string Kdf, int Iterations, string SaltBase64, string NoncePrefixBase64,
    int ChunkSize, long PlaintextLength, string PlaintextSha256, string Note);

/// <summary>
/// Encrypted backup container (docs/02 — sao lưu): magic "HAKBACKUP1\n", header length + JSON header, then chunks
/// [isLast byte][int32 length][ciphertext][16-byte tag]. AES-256-GCM per chunk; key = PBKDF2-SHA256(passphrase, salt);
/// nonce = 8-byte random prefix + chunk index; associated data = SHA-256(header) + index + isLast, so a modified header, a
/// reordered / altered chunk and a truncated file are all rejected. The plaintext SHA-256 is checked again after decryption.
/// </summary>
public static class BackupFile
{
    private static readonly byte[] Magic = "HAKBACKUP1\n"u8.ToArray();
    public const int DefaultIterations = 600_000;
    public const int DefaultChunkSize = 1 << 20;
    private const int TagSize = 16;

    public static BackupHeader Encrypt(Stream plaintext, Stream output, string passphrase, string sourceProvider, string note, DateTime createdAt,
        int iterations = DefaultIterations, int chunkSize = DefaultChunkSize)
    {
        // First pass: length + hash, so they can be authenticated in the header.
        var start = plaintext.Position;
        var sha = SHA256.HashData(plaintext);
        var length = plaintext.Position - start;
        plaintext.Position = start;

        var salt = RandomNumberGenerator.GetBytes(16);
        var prefix = RandomNumberGenerator.GetBytes(8);
        var header = new BackupHeader(1, createdAt, sourceProvider, "PBKDF2-SHA256", iterations, Convert.ToBase64String(salt), Convert.ToBase64String(prefix),
            chunkSize, length, Convert.ToHexStringLower(sha), note);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);
        output.Write(Magic);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, headerBytes.Length);
        output.Write(len);
        output.Write(headerBytes);

        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        using var aes = new AesGcm(key, TagSize);
        var headerHash = SHA256.HashData(headerBytes);
        var buffer = new byte[chunkSize];
        var cipher = new byte[chunkSize];
        var tag = new byte[TagSize];
        var index = 0;
        var remaining = length;
        do
        {
            var n = ReadFull(plaintext, buffer, (int)Math.Min(chunkSize, remaining));
            remaining -= n;
            var isLast = remaining == 0;
            aes.Encrypt(Nonce(prefix, index), buffer.AsSpan(0, n), cipher.AsSpan(0, n), tag, Aad(headerHash, index, isLast));
            output.WriteByte(isLast ? (byte)1 : (byte)0);
            BinaryPrimitives.WriteInt32LittleEndian(len, n);
            output.Write(len);
            output.Write(cipher, 0, n);
            output.Write(tag);
            index++;
        } while (remaining > 0);
        CryptographicOperations.ZeroMemory(key);
        return header;
    }

    /// <summary>Reads the header without the passphrase (for listings). Not trusted until <see cref="Decrypt"/> succeeds.</summary>
    public static BackupHeader ReadHeader(Stream input) => ReadHeaderCore(input).Header;

    public static BackupHeader Decrypt(Stream input, Stream output, string passphrase)
    {
        var (header, headerBytes) = ReadHeaderCore(input);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, Convert.FromBase64String(header.SaltBase64), header.Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            var prefix = Convert.FromBase64String(header.NoncePrefixBase64);
            var headerHash = SHA256.HashData(headerBytes);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> len = stackalloc byte[4];
            var tag = new byte[TagSize];
            long written = 0;
            for (var index = 0; ; index++)
            {
                var flag = input.ReadByte();
                if (flag < 0) throw Invalid("File sao lưu bị cắt cụt (thiếu khối cuối).");
                if (ReadFull(input, len) != 4) throw Invalid("File sao lưu bị cắt cụt.");
                var n = BinaryPrimitives.ReadInt32LittleEndian(len);
                if (n < 0 || n > header.ChunkSize) throw Invalid("Khối dữ liệu sao lưu không hợp lệ.");
                var cipher = new byte[n];
                var plain = new byte[n];
                if (ReadFull(input, cipher) != n || ReadFull(input, tag) != TagSize) throw Invalid("File sao lưu bị cắt cụt.");
                try { aes.Decrypt(Nonce(prefix, index), cipher, tag, plain, Aad(headerHash, index, flag == 1)); }
                catch (AuthenticationTagMismatchException) { throw Invalid("Sai mật khẩu sao lưu hoặc file đã bị sửa đổi."); }
                output.Write(plain);
                hash.AppendData(plain);
                written += n;
                if (flag == 1) break;
            }
            if (input.ReadByte() >= 0) throw Invalid("Có dữ liệu lạ sau khối cuối của file sao lưu.");
            if (written != header.PlaintextLength || Convert.ToHexStringLower(hash.GetHashAndReset()) != header.PlaintextSha256)
                throw Invalid("Mã kiểm tra SHA-256 của dữ liệu khôi phục không khớp.");
            return header;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static (BackupHeader Header, byte[] Bytes) ReadHeaderCore(Stream input)
    {
        var magic = new byte[Magic.Length];
        if (ReadFull(input, magic) != Magic.Length || !magic.AsSpan().SequenceEqual(Magic)) throw Invalid("Không phải file sao lưu HAK.");
        Span<byte> len = stackalloc byte[4];
        if (ReadFull(input, len) != 4) throw Invalid("File sao lưu bị cắt cụt.");
        var size = BinaryPrimitives.ReadInt32LittleEndian(len);
        if (size is <= 0 or > 64 * 1024) throw Invalid("Phần đầu file sao lưu không hợp lệ.");
        var bytes = new byte[size];
        if (ReadFull(input, bytes) != size) throw Invalid("File sao lưu bị cắt cụt.");
        try { return (JsonSerializer.Deserialize<BackupHeader>(bytes) ?? throw Invalid("Phần đầu file sao lưu rỗng."), bytes); }
        catch (JsonException) { throw Invalid("Phần đầu file sao lưu không đọc được."); }
    }

    private static byte[] Nonce(byte[] prefix, int index)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt32BigEndian(nonce.AsSpan(8), index);
        return nonce;
    }

    private static byte[] Aad(byte[] headerHash, int index, bool isLast)
    {
        var aad = new byte[headerHash.Length + 5];
        headerHash.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt32BigEndian(aad.AsSpan(headerHash.Length), index);
        aad[^1] = isLast ? (byte)1 : (byte)0;
        return aad;
    }

    private static int ReadFull(Stream s, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = s.Read(buffer[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static int ReadFull(Stream s, byte[] buffer, int count) => ReadFull(s, buffer.AsSpan(0, count));

    private static DomainException Invalid(string message) => new(ErrorCodes.BackupInvalid, message);
}
