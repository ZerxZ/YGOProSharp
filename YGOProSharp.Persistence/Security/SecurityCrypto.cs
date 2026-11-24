using System.Net;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace YGOProSharp.Persistence.Security;

/// <summary>
/// Argon2id 密码哈希器与密码强度核验器。
/// </summary>
public sealed class Argon2PasswordHasher
{
    private const int SaltLengthBytes = 16;
    private const int HashLengthBytes = 32;

    private static readonly HashSet<string> WeakPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890",
        "12345678901",
        "123456789012",
        "0000000000",
        "password123",
        "password1234",
        "admin12345",
        "admin123456",
        "qwertyuiop",
        "qwerty12345",
        "ygopro12345",
        "ygoprosharp",
        "changeme123",
        "letmein1234",
        "welcome1234"
    };

    private readonly int _memoryKb;
    private readonly int _iterations;
    private readonly int _parallelism;

    public Argon2PasswordHasher(int memoryKb = 19456, int iterations = 2, int parallelism = 1)
    {
        _memoryKb = Math.Clamp(memoryKb, 4096, 262144);
        _iterations = Math.Clamp(iterations, 1, 16);
        _parallelism = Math.Clamp(parallelism, 1, 8);
    }

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        byte[] hash = ComputeArgon2id(password, salt, _memoryKb, _iterations, _parallelism, HashLengthBytes);
        string saltB64 = Convert.ToBase64String(salt);
        string hashB64 = Convert.ToBase64String(hash);
        return $"$argon2id$v=19$m={_memoryKb},t={_iterations},p={_parallelism}${saltB64}${hashB64}";
    }

    public bool VerifyPassword(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
            return false;

        string[] parts = encodedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || !string.Equals(parts[0], "argon2id", StringComparison.Ordinal))
            return false;

        int memoryKb = _memoryKb;
        int iterations = _iterations;
        int parallelism = _parallelism;

        foreach (string kv in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kv.StartsWith("m=", StringComparison.Ordinal) && int.TryParse(kv.AsSpan(2), out int m))
                memoryKb = m;
            else if (kv.StartsWith("t=", StringComparison.Ordinal) && int.TryParse(kv.AsSpan(2), out int t))
                iterations = t;
            else if (kv.StartsWith("p=", StringComparison.Ordinal) && int.TryParse(kv.AsSpan(2), out int p))
                parallelism = p;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length < 8 || expectedHash.Length < 16)
            return false;

        byte[] actualHash = ComputeArgon2id(password, salt, memoryKb, iterations, parallelism, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public static (bool IsValid, string? ErrorMessage) ValidatePasswordStrength(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            return (false, "密码长度至少需要 10 位字符。");

        if (password.Length > 128)
            return (false, "密码长度不能超过 128 位字符。");

        if (WeakPasswords.Contains(password.Trim()))
            return (false, "禁止使用常见弱口令，请使用更高复杂度的密码。");

        if (!string.IsNullOrWhiteSpace(username) &&
            password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return (false, "密码不能直接包含账号用户名。");
        }

        int categories = 0;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(static c => !char.IsLetterOrDigit(c))) categories++;

        if (categories < 2)
            return (false, "密码至少需要包含大写字母、小写字母、数字或特殊符号中的两类。");

        return (true, null);
    }

    private static byte[] ComputeArgon2id(string password, byte[] salt, int memoryKb, int iterations, int parallelism, int outputBytes)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using Argon2id argon2 = new(passwordBytes)
            {
                Salt = salt,
                MemorySize = memoryKb,
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };
            return argon2.GetBytes(outputBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}

/// <summary>
/// 令牌生成、单向 SHA-256 摘要计算与常量时间比对工具（参考 EDOpro-server-ts 防时序侧信道）。
/// </summary>
public static class TokenCrypto
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*-_=+";
    private const string AlphanumericAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string GenerateBootstrapPassword(int length = 24)
    {
        int actualLength = Math.Clamp(length, 16, 64);
        Span<char> chars = stackalloc char[actualLength];
        for (int i = 0; i < actualLength; i++)
            chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];

        // 确保至少包含大小写、数字和符号以确保必过强度校验
        chars[0] = 'A';
        chars[1] = 'z';
        chars[2] = '9';
        chars[3] = '#';
        return new string(chars);
    }

    public static (string RawToken, string Prefix, string Sha256Hex) GeneratePrefixedToken(string prefix)
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        string body = Base64UrlEncode(randomBytes);
        string raw = $"{prefix}_{body}";
        string tokenPrefix = raw.Length <= 14 ? raw : raw[..14];
        string sha256 = ComputeSha256Hex(raw);
        return (raw, tokenPrefix, sha256);
    }

    public static string GenerateNonce(int byteLength = 24)
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(Math.Clamp(byteLength, 12, 64)));
    }

    public static string ComputeSha256Hex(string input)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(input ?? string.Empty);
        byte[] digest = SHA256.HashData(utf8);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// 对两个十六进制 SHA-256 摘要执行常量时间比较（防时序侧信道攻击）。
    /// </summary>
    public static bool FixedTimeEqualsSha256Hex(string candidateSha256Hex, string storedSha256Hex)
    {
        if (string.IsNullOrEmpty(candidateSha256Hex) || string.IsNullOrEmpty(storedSha256Hex))
            return false;

        if (candidateSha256Hex.Length != 64 || storedSha256Hex.Length != 64)
            return false;

        byte[] left;
        byte[] right;
        try
        {
            left = Convert.FromHexString(candidateSha256Hex);
            right = Convert.FromHexString(storedSha256Hex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static bool FixedTimeEqualsString(string left, string right)
    {
        byte[] leftDigest = SHA256.HashData(Encoding.UTF8.GetBytes(left ?? string.Empty));
        byte[] rightDigest = SHA256.HashData(Encoding.UTF8.GetBytes(right ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(leftDigest, rightDigest) &&
               string.Equals(left, right, StringComparison.Ordinal);
    }

    public static string GenerateShortCode(int length = 8)
    {
        int actual = Math.Clamp(length, 4, 24);
        Span<char> chars = stackalloc char[actual];
        for (int i = 0; i < actual; i++)
            chars[i] = AlphanumericAlphabet[RandomNumberGenerator.GetInt32(AlphanumericAlphabet.Length)];
        return new string(chars);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

/// <summary>
/// IP 脱敏（直播防泄漏）与 IP / CIDR 封禁匹配辅助工具。
/// </summary>
public static class IpPrivacyHelper
{
    /// <summary>
    /// 按调用方权限裁剪 IP：无 <c>room:view_ip</c> 权限或处于 <c>streamer</c> 主播只读角色时，强制返回 <c>null</c> 彻底剥离 IP。
    /// </summary>
    public static string? SanitizeIp(string? rawIp, bool canViewIp, bool isStreamerRole)
    {
        if (string.IsNullOrWhiteSpace(rawIp) || !canViewIp || isStreamerRole)
            return null;

        return rawIp.Trim();
    }

    public static bool MatchesIpOrCidr(string? clientIp, string targetType, string targetValue)
    {
        if (string.IsNullOrWhiteSpace(clientIp) || string.IsNullOrWhiteSpace(targetValue))
            return false;

        if (!IPAddress.TryParse(clientIp.Trim(), out IPAddress? ip))
            return false;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        string normalizedTarget = targetValue.Trim();
        if (string.Equals(targetType, "ip", StringComparison.OrdinalIgnoreCase))
        {
            if (!IPAddress.TryParse(normalizedTarget, out IPAddress? targetIp))
                return false;
            if (targetIp.IsIPv4MappedToIPv6)
                targetIp = targetIp.MapToIPv4();
            return ip.Equals(targetIp);
        }

        if (string.Equals(targetType, "cidr", StringComparison.OrdinalIgnoreCase))
        {
            if (IPNetwork.TryParse(normalizedTarget, out IPNetwork network))
                return network.Contains(ip);
        }

        return false;
    }
}
