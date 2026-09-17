using System;
using System.Security.Cryptography;
using System.Text;

namespace Configuration_Management.Services;

/// <summary>
/// Хэширование/проверка паролей алгоритмом PBKDF2-SHA256 с случайной солью
/// (функция №19 — временная блокировка приложения паролем). Чистый .NET без
/// UI-зависимостей. Формат результата: «<c>итерации.сольBase64.хэшBase64</c>» —
/// совместим с хэшами паролей профилей (<see cref="ProfileService"/>).
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;

    /// <summary>Хэширует пароль (пустой пароль даёт пустой хэш).</summary>
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            return string.Empty;

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>Проверяет пароль против сохранённого хэша.</summary>
    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return true; // пароль не задан — блокировка не требуется
        if (string.IsNullOrEmpty(password))
            return false;

        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3)
                return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}