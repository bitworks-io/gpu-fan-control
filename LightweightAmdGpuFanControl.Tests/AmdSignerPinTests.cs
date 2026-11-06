using LightweightAmdGpuFanControl.Drivers;
using Xunit;

public class AmdSignerPinTests
{
    private const string Subject = "CN=Advanced Micro Devices, O=Advanced Micro Devices, S=California, C=US";
    private const string Issuer = "CN=Sectigo Public Code Signing CA R36, O=Sectigo Limited, C=GB";

    [Fact]
    public void Matches_AmdSubjectAndSectigoIssuer_True() => Assert.True(AmdSignerPin.Matches(Subject, Issuer));

    [Fact]
    public void Matches_QuotedValueWithComma_StillParsed()
    {
        Assert.True(AmdSignerPin.Matches("CN=\"Advanced Micro Devices, Inc.\", O=Advanced Micro Devices, C=US", Issuer));
    }

    [Fact]
    public void Matches_CommaInsideQuotesDoesNotForgeRdn()
    {
        Assert.False(AmdSignerPin.Matches("CN=\"x, O=Advanced Micro Devices\", O=Evil", Issuer));
    }

    [Fact]
    public void Matches_SubjectWithSuffix_False() =>
        Assert.False(AmdSignerPin.Matches("O=Advanced Micro Devices Inc, C=US", Issuer));

    [Fact]
    public void Matches_OtherSubject_False() =>
        Assert.False(AmdSignerPin.Matches("CN=Microsoft Windows, O=Microsoft Corporation", Issuer));

    [Fact]
    public void Matches_OtherIssuer_False() =>
        Assert.False(AmdSignerPin.Matches(Subject, "CN=Sectigo Public Code Signing CA R35, O=Sectigo Limited"));

    [Fact]
    public void Matches_NullOrEmpty_False()
    {
        Assert.False(AmdSignerPin.Matches(null, Issuer));
        Assert.False(AmdSignerPin.Matches(Subject, null));
        Assert.False(AmdSignerPin.Matches("", ""));
    }
}
