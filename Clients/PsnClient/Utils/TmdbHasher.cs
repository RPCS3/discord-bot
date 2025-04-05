using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PsnClient.Utils;

public static class TmdbHasher
{
    private static readonly byte[] HmacKey = "F5DE66D2680E255B2DF79E74F890EBF349262F618BCAE2A9ACCDEE5156CE8DF2CDF2D48C71173CDC2594465B87405D197CF1AED3B7E9671EEB56CA6753C2E6B0".FromHexString();

    public static string GetTitleHash(string productId)
        => HMACSHA1.HashData(HmacKey, Encoding.UTF8.GetBytes(productId)).ToHexString();

    public static byte[] FromHexString(this string hexString)
    {
        if (hexString.Length == 0)
            return [];

        if (hexString.Length % 2 != 0)
            throw new ArgumentException("Invalid hex string format: odd number of octets", nameof(hexString));

        var result = new byte[hexString.Length/2];
        for (int i = 0, j = 0; i < hexString.Length; i += 2, j++)
            result[j] = byte.Parse(hexString.Substring(i, 2), NumberStyles.HexNumber);
        return result;
    }

    public static string ToHexString(this byte[] array)
    {
        if (array.Length == 0)
            return "";

        var result = new StringBuilder(array.Length*2);
        foreach (var b in array)
            result.Append(b.ToString("X2"));
        return result.ToString();
    }
}