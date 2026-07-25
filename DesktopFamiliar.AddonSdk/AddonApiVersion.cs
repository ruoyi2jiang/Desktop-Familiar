namespace DesktopFamiliar.AddonSdk;

/// <summary>A versioned, binary-compatible contract between Desktop Familiar and an addon.</summary>
public readonly record struct AddonApiVersion(int Major, int Minor) : IComparable<AddonApiVersion>
{
    public int CompareTo(AddonApiVersion other)
    {
        var major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>The API version exposed by the currently running addon host.</summary>
public static class AddonSdkVersion
{
    /// <summary>
    /// 1.3 adds optional inline installed-addon settings composition.
    /// </summary>
    public static readonly AddonApiVersion CurrentHostApi = new(1, 3);
}
