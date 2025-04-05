using System;
using System.Collections.Generic;
using CompatApiClient.POCOs;

namespace CompatApiClient;

public class UpdateInfo
{
    public UpdateCheckResult? X64;
    public UpdateCheckResult? Arm;

    public UpdateCheckResult? this[string key]
    {
        get => key switch
        {
            ArchType.X64 => X64,
            ArchType.Arm => Arm,
            _ => throw new KeyNotFoundException($"Unknown {nameof(ArchType)} '{key}'")
        };
        set
        {
            if (key is ArchType.X64)
                X64 = value;
            else if (key is ArchType.Arm)
                Arm = value;
            else
                throw new KeyNotFoundException($"Unknown {nameof(ArchType)} '{key}'");
        }
    }

    public void SetCurrentAsLatest()
    {
        if (this is {X64.CurrentBuild: not null, Arm.CurrentBuild: not null})
        {
            X64.LatestBuild = X64.CurrentBuild;
            X64.CurrentBuild = null;
            Arm.LatestBuild = Arm.CurrentBuild;
            Arm.CurrentBuild = null;
        }
        else if (X64?.CurrentBuild is not null)
        {
            X64.LatestBuild = X64.CurrentBuild;
            X64.CurrentBuild = null;
            Arm = null;
        }
        else if (Arm?.CurrentBuild is not null)
        {
            Arm.LatestBuild = Arm.CurrentBuild;
            Arm.CurrentBuild = null;
            X64 = null;
        }
    }
    
    public StatusCode ReturnCode => (X64?.ReturnCode, Arm?.ReturnCode) switch
    {
        ({ } v1, { } v2) when v1 == v2 => v1,
        ({ } v1 and >= StatusCode.UnknownBuild, { } v2 and >= StatusCode.UnknownBuild) => (StatusCode)Math.Max((int)v1,
            (int)v2),
        ({ }, { }) => StatusCode.Maintenance,
        ({ } v, null) => v,
        (null, { } v) => v,
        _ => StatusCode.Maintenance,
    };

    public DateTime? LatestDatetime => ((X64?.LatestBuild.Datetime, Arm?.LatestBuild.Datetime) switch
    {
        ({ Length: > 0 } d1, { Length: > 0 } d2) => StringComparer.Ordinal.Compare(d1, d1) >= 0 ? d1 : d2,
        ({ Length: > 0 } d, _) => d,
        (_, { Length: > 0 } d) => d,
        _ => null,
    }) switch
    {
        { Length: > 0 } v when DateTime.TryParse(v, out var result) => result,
        _ => null,
    };

    public DateTime? CurrentDatetime => ((X64?.CurrentBuild?.Datetime, Arm?.CurrentBuild?.Datetime) switch
    {
        ({ Length: > 0 } d1, { Length: > 0 } d2) => StringComparer.Ordinal.Compare(d1, d1) >= 0 ? d1 : d2,
        ({ Length: > 0 } d, _) => d,
        (_, { Length: > 0 } d) => d,
        _ => null,
    }) switch
    {
        { Length: > 0 } v when DateTime.TryParse(v, out var result) => result,
        _ => null,
    };

    public int? LatestPr => (X64?.LatestBuild.Pr, Arm?.LatestBuild.Pr) switch
    {
        //(int pr1, int pr2) when pr1 != pr2 => throw new InvalidDataException($"Expected the same PR for both {nameof(ArchType)}, but got {pr1} and {pr2}"),
        (int pr, _) => pr,
        (_, int pr) => pr,
        _ => null,
    };
}