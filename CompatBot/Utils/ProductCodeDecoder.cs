using System;
using System.Collections.Generic;

namespace CompatBot.Utils;

public static class ProductCodeDecoder
{
    public static List<string> Decode(in string productCode)
    {
        var result = new List<string>(4);
        if (string.IsNullOrEmpty(productCode) || productCode.Length < 4)
            result.Add("Invalid product code");
        else
            DecodeMedia(productCode.ToUpperInvariant().AsSpan(), result);
        return result;
    }

    private static void DecodeMedia(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'S':
            {
                result.Add("CD/DVD optical media for Playstation 1, 2, or 3");
                break;
            }
            case 'U':
            {
                result.Add("UMD optical media for Playstation Portable");
                break;
            }
            case 'B':
            {
                result.Add("Blu-ray optical media for Playstation 3");
                break;
            }
            case 'N':
            {
                DecodeMediaN(productCode[1..], result);
                return;
            }
            case 'X':
            {
                result.Add("Mixed blu-ray optical media with extras for Playstation 3/Vita cross-buy");
                break;
            }
            case 'V':
            {
                result.Add("Playstation Vita game card");
                break;
            }
            case 'P':
            {
                DecodeMediaP(productCode[1..], result);
                return;
            }
            case 'C':
            {
                DecodeMediaC(productCode[1..], result);
                return;
            }
            case char _ when productCode[..4] == "MRTC":
            {
                result.Add("Media Replication and Transfer Code (or something completely different, no one knows for sure)");
                DecodeRegionNumbers(productCode[4..], result);
                return;
            }
            default:
            {
                result.Add("Unknown or invalid product code");
                return;
            }
        }
        DecodeRights(productCode[1..], result);
    }

    private static void DecodeRights(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'C':
            {
                result.Add("Copyrighted by Sony (first party title)");
                break;
            }
            case 'L':
            {
                result.Add("Licensed to Sony (third party title)");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid licensing model");
                break;
            }
        }
        DecodePhysicalRegion(productCode[1..], result);
    }

    private static void DecodePhysicalRegion(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'A':
            {
                result.Add("Asia");
                break;
            }
            case 'C':
            {
                result.Add("China");
                break;
            }
            case 'E':
            {
                result.Add("Europe");
                break;
            }
            case 'H':
            {
                result.Add("Hong Kong");
                break;
            }
            case 'J':
            case 'P':
            {
                result.Add("Japan");
                break;
            }
            case 'K':
            {
                result.Add("Korea");
                break;
            }
            case 'U':
            {
                result.Add("US");
                break;
            }
            case 'I':
            {
                result.Add("System software");
                break;
            }
            case 'X':
            {
                result.Add("Example SDK software");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid region designation");
                break;
            }
        }
        DecodePhysicalContentType(productCode[1..], result);
    }

    private static void DecodePhysicalContentType(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'B':
            {
                result.Add("Peripheral software");
                break;
            }
            case 'C':
            {
                result.Add("System software");
                break;
            }
            case 'D':
            {
                result.Add("Demo");
                break;
            }
            case 'M':
            {
                result.Add("Malayan release");
                break;
            }
            case 'S':
            {
                result.Add("Retail release");
                break;
            }
            case 'T':
            {
                result.Add("Closed beta release");
                break;
            }
            case 'X':
            {
                result.Add("Special not for sale install disc");
                break;
            }
            case 'V':
            {
                result.Add("Multi-region custom software for Playstation 3");
                break;
            }
            case 'Z':
            {
                result.Add("Region-locked custom software for Playstation 3");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid media type");
                break;
            }
        }
        DecodeRegionNumbers(productCode[1..], result);
    }

    private static void DecodeRegionNumbers(in ReadOnlySpan<char> productCode, List<string> result)
    {
        if (productCode.IsEmpty || productCode.Length < 2)
            return;

        switch (productCode)
        {
            case ['0', '0', ..]:
            case ['0', '1', ..]:
            case ['0', '2', ..]:
            {
                result.Add("European code range");
                break;
            }
            case ['2', '0', ..]:
            {
                result.Add("Korean code range");
                break;
            }
            case ['3', '0', ..]:
            case ['3', '1', ..]:
            case ['4', '1', ..]:
            case ['8', '1', ..]:
            case ['8', '2', ..]:
            case ['8', '3', ..]:
            case ['9', '0', ..]:
            case ['9', '1', ..]:
            case ['9', '4', ..]:
            case ['9', '8', ..]:
            case ['9', '9', ..]:
            {
                result.Add("USA code range");
                break;
            }
            case ['5', '0', ..]:
            {
                result.Add("Asian code range");
                break;
            }
            case ['6', '0', ..]:
            case ['6', '1', ..]:
            {
                result.Add("Japanese code range");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid code range");
                break;
            }
        }
    }

    private static void DecodeMediaN(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'P':
            {
                result.Add("Playstation Network digital release for Playstation 3");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid media type");
                return;
            }
        }
        DecodeDigitalRegion(productCode[1..], result);
    }

    private static void DecodeDigitalRegion(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'A':
            {
                result.Add("Asia");
                break;
            }
            case 'E':
            {
                result.Add("Europe");
                break;
            }
            case 'H':
            {
                result.Add("Hong Kong");
                break;
            }
            case 'J':
            {
                result.Add("Japan");
                break;
            }
            case 'K':
            {
                result.Add("Korea");
                break;
            }
            case 'U':
            {
                result.Add("US");
                break;
            }
            case 'I':
            {
                result.Add("System software");
                break;
            }
            case 'X':
            {
                result.Add("Example SDK software");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid region designation");
                break;
            }
        }
        DecodeDigitalContentType(productCode[1..], result);
    }

    private static void DecodeDigitalContentType(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'A':
            {
                result.Add("First party Playstation 3 software");
                break;
            }
            case 'B':
            {
                result.Add("Licensed third party Playstation 3 software");
                break;
            }
            case 'C':
            {
                result.Add("First party Playstation 2 Classic software");
                break;
            }
            case 'D':
            {
                result.Add("Licensed third party Playstation 2 Classic software");
                break;
            }
            case 'E':
            {
                result.Add("First party Playstation One Classic software (PAL)");
                break;
            }
            case 'F':
            {
                result.Add("Licensed third party Playstation One Classic software (PAL)");
                break;
            }
            case 'G':
            {
                result.Add("First party Playstation Portable software");
                break;
            }
            case 'H':
            {
                result.Add("First party Playstation Portable software");
                break;
            }
            case 'I':
            {
                result.Add("First party Playstation One Classic software (NTSC)");
                break;
            }
            case 'J':
            {
                result.Add("Licensed third party Playstation One Classic software (NTSC)");
                break;
            }
            case 'K':
            {
                result.Add("First party game related content");
                break;
            }
            case 'L':
            {
                result.Add("Licensed third party game related content");
                break;
            }
            case 'M':
            {
                result.Add("Music");
                break;
            }
            case 'N':
            {
                result.Add("Game soundtrack");
                break;
            }
            case 'O':
            {
                result.Add("Miscellaneous software (manuals, etc)");
                break;
            }
            case 'P':
            {
                result.Add("Applications (dynamic themes, streaming services, etc)");
                break;
            }
            case 'Q':
            {
                result.Add("XMB Theme");
                break;
            }
            case 'S':
            {
                result.Add("System software");
                break;
            }
            case 'W':
            {
                result.Add("First party Playstation Portable Remaster software");
                break;
            }
            case 'X':
            {
                result.Add("First party Playstation Minis software");
                break;
            }
            case 'Y':
            {
                result.Add("Licensed third party Playstation Portable Remaster software");
                break;
            }
            case 'Z':
            {
                result.Add("Licensed third party Playstation Minis software");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid content type");
                break;
            }
        }
        DecodeRegionNumbers(productCode[1..], result);
    }

    private static void DecodeMediaP(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'E':
            {
                result.Add("CD/DVD optical media with custom software for Playstation 1/2 in Europe, Australia, and Gulf area");
                break;
            }
            case 'T':
            {
                result.Add("CD/DVD optical media with custom software for Playstation 1/2 in Japan, and Asia");
                break;
            }
            case 'U':
            {
                result.Add("CD/DVD optical media with custom software for Playstation 1/2 in US, and Canada");
                break;
            }
            case 'C':
            {
                DecodeMediaPC(productCode[1..], result);
                return;
            }
            default:
            {
                result.Add("Unknown or invalid media type");
                return;
            }
        }
        DecodePhysicalRegion(productCode[1..], result);
    }

    private static void DecodeMediaPC(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'S':
            {
                result.Add("Playstation Vita software");
                DecodeVitaRegion(productCode[1..], result);
                return;
            }
            case 'P':
            {
                result.Add("Optical media with promotional videos");
                DecodePhysicalContentType(productCode[1..], result);
                return;
            }
            default:
            {
                result.Add("Unknown or invalid media type");
                return;
            }
        }
    }

    private static void DecodeVitaRegion(ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode[0])
        {
            case 'A':
            case 'E':
            {
                result.Add("US");
                break;
            }
            case 'B':
            case 'F':
            {
                result.Add("Europe");
                break;
            }
            case 'C':
            case 'G':
            {
                result.Add("Japan");
                break;
            }
            case 'D':
            case 'H':
            {
                result.Add("Asia");
                break;
            }
            case 'I':
            {
                result.Add("System software");
                break;
            }
            default:
            {
                result.Add("Unknown or invalid region code");
                break;
            }
        }
    }

    private static void DecodeMediaC(in ReadOnlySpan<char> productCode, List<string> result)
    {
        switch (productCode)
        {
            case ['U', 'S', 'A', ..]:
            {
                result.Add("Playstation 4 software");
                return;
            }
            default:
            {
                result.Add("Unknown or invalid media type");
                return;
            }
        }
    }
}