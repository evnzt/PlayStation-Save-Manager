using System;
using System.Security.Cryptography;

namespace PlayStationSaveManager.Services;

internal static class Ps1FormatCrypto
{
    private static readonly byte[] SaveKey =
    {
        0xAB, 0x5A, 0xBC, 0x9F, 0xC1, 0xF4, 0x9D, 0xE6,
        0xA0, 0x51, 0xDB, 0xAE, 0xFA, 0x51, 0x88, 0x59
    };

    private static readonly byte[] SaveIv =
    {
        0xB3, 0x0F, 0xFE, 0xED, 0xB7, 0xDC, 0x5E, 0xB7,
        0x13, 0x3D, 0xA6, 0x0D, 0x1B, 0x6B, 0x2C, 0xDC
    };

    public static byte[] ComputeSignature(
        byte[] data,
        byte[] saltSeed)
    {
        if (saltSeed.Length != 20)
            throw new ArgumentException("PS1 PSV/VMP salt seeds must be 20 bytes.", nameof(saltSeed));

        var buffer = new byte[20];
        var salt = new byte[64];
        var temp = new byte[20];

        Buffer.BlockCopy(saltSeed, 0, buffer, 0, 20);
        Buffer.BlockCopy(AesEcb(buffer.AsSpan(0, 16).ToArray(), decrypt: true), 0, buffer, 0, 16);
        Buffer.BlockCopy(buffer, 0, salt, 0, 16);

        Buffer.BlockCopy(saltSeed, 0, buffer, 0, 16);
        Buffer.BlockCopy(AesEcb(buffer.AsSpan(0, 16).ToArray(), decrypt: false), 0, buffer, 0, 16);
        Buffer.BlockCopy(buffer, 0, salt, 16, 16);

        for (var index = 0; index < 16; index++)
            salt[index] ^= SaveIv[index];

        Array.Fill(buffer, (byte)0xFF);
        Buffer.BlockCopy(saltSeed, 16, buffer, 0, 4);
        Buffer.BlockCopy(salt, 16, temp, 0, 20);

        for (var index = 0; index < 16; index++)
            temp[index] ^= buffer[index];

        Buffer.BlockCopy(temp, 0, salt, 16, 16);
        Buffer.BlockCopy(salt, 0, temp, 0, 20);
        Array.Clear(salt, 0, salt.Length);
        Buffer.BlockCopy(temp, 0, salt, 0, 20);

        for (var index = 0; index < salt.Length; index++)
            salt[index] ^= 0x36;

        byte[] inner;
        using (var sha1 = SHA1.Create())
        {
            var input = new byte[salt.Length + data.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(data, 0, input, salt.Length, data.Length);
            inner = sha1.ComputeHash(input);
        }

        for (var index = 0; index < salt.Length; index++)
            salt[index] ^= 0x6A;

        using (var sha1 = SHA1.Create())
        {
            var input = new byte[salt.Length + inner.Length];
            Buffer.BlockCopy(salt, 0, input, 0, salt.Length);
            Buffer.BlockCopy(inner, 0, input, salt.Length, inner.Length);
            return sha1.ComputeHash(input);
        }
    }

    public static void SignPsv(byte[] psv)
    {
        if (psv.Length < 0x84)
            throw new InvalidOperationException("The PSV image is too small to sign.");

        Array.Clear(psv, 0x08, 0x28);

        byte[] seed;
        using (var sha1 = SHA1.Create())
            seed = sha1.ComputeHash(psv);

        Buffer.BlockCopy(seed, 0, psv, 0x08, 20);
        var signature = ComputeSignature(psv, seed);
        Buffer.BlockCopy(signature, 0, psv, 0x1C, 20);
    }

    public static void SignVmp(byte[] vmp)
    {
        if (vmp.Length < 0x80)
            throw new InvalidOperationException("The VMP image is too small to sign.");

        Array.Clear(vmp, 0x0C, 0x28);

        byte[] seed;
        using (var sha1 = SHA1.Create())
            seed = sha1.ComputeHash(vmp);

        Buffer.BlockCopy(seed, 0, vmp, 0x0C, 20);
        var signature = ComputeSignature(vmp, seed);
        Buffer.BlockCopy(signature, 0, vmp, 0x20, 20);
    }

    private static byte[] AesEcb(byte[] input, bool decrypt)
    {
        using var aes = Aes.Create();
        aes.Key = SaveKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var transform = decrypt
            ? aes.CreateDecryptor()
            : aes.CreateEncryptor();

        return transform.TransformFinalBlock(input, 0, input.Length);
    }
}
