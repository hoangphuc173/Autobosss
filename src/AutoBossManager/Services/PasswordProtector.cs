using System;
using System.Security.Cryptography;
using System.Text;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Ma hoa / giai ma password bang Windows DPAPI (DataProtectionScope.CurrentUser).
    /// - Chi may + user tao ra co the giai ma.
    /// - Format luu tru: "enc:v1:" + base64(cipher). Moi chuoi khac = plaintext (legacy, tu dong migrate khi save).
    /// </summary>
    public static class PasswordProtector
    {
        private const string Prefix = "enc:v1:";

        public static string Protect(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                return plainPassword ?? string.Empty;
            }

            if (IsEncrypted(plainPassword))
            {
                return plainPassword; // already protected
            }

            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainPassword),
                null,
                DataProtectionScope.CurrentUser);

            return Prefix + Convert.ToBase64String(cipher);
        }

        public static string Unprotect(string storedPassword)
        {
            if (string.IsNullOrEmpty(storedPassword))
            {
                return storedPassword ?? string.Empty;
            }

            if (!IsEncrypted(storedPassword))
            {
                return storedPassword; // legacy plaintext
            }

            try
            {
                var plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(storedPassword[Prefix.Length..]),
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                // Ma hoa bo vo hoac khong giai ma duoc tren may nay.
                return string.Empty;
            }
        }

        public static bool IsEncrypted(string value) =>
            value.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
