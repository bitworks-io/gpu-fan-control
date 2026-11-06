namespace LightweightAmdGpuFanControl.Drivers;

/// <summary>
/// Signer pin for AMD driver libraries: the subject must carry the exact RDN
/// <c>O=Advanced Micro Devices</c> and the issuer the exact RDN
/// <c>CN=Sectigo Public Code Signing CA R36</c>. Whole-RDN, ordinal comparison.
/// </summary>
public static class AmdSignerPin
{
    public const string SubjectRdn = "O=Advanced Micro Devices";
    public const string IssuerRdn = "CN=Sectigo Public Code Signing CA R36";

    public static bool Matches(string? subject, string? issuer)
    {
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(issuer)) return false;
        return HasRdn(subject!, SubjectRdn) && HasRdn(issuer!, IssuerRdn);
    }

    private static bool HasRdn(string dn, string wanted)
    {
        foreach (var rdn in SplitDn(dn))
            if (string.Equals(rdn, wanted, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Splits a distinguished name on commas outside double quotes; trims each RDN.</summary>
    private static List<string> SplitDn(string dn)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in dn)
        {
            if (c == '"') { inQuotes = !inQuotes; cur.Append(c); }
            else if (c == ',' && !inQuotes) { result.Add(cur.ToString().Trim()); cur.Clear(); }
            else cur.Append(c);
        }
        result.Add(cur.ToString().Trim());
        return result;
    }
}
