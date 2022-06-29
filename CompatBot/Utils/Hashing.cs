using System;

namespace CompatBot.Utils;

public static class Hashing
{
    public static byte[] GetSaltedHash(this byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        if (data.Length > 0)
            sha256.TransformBlock(data, 0, data.Length, null, 0);
        sha256.TransformFinalBlock(Config.CryptoSalt, 0, Config.CryptoSalt.Length);
        return sha256.Hash ?? Guid.Empty.ToByteArray();
    }
}