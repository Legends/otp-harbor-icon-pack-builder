using OtpHarbor.IconPackBuilder.Normalization;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class NormalizationTests
{
    [Theory]
    [InlineData("  GitHub  ", "github")]
    [InlineData("ＡＣＭＥ   Cloud", "acme cloud")]
    [InlineData("Amazon_Web-Services / EU", "amazon web services eu")]
    [InlineData("C++", "c++")]
    [InlineData("AT&T", "at&t")]
    public void IssuerNormalizationIsConservativeAndDeterministic(string input, string expected)
        => Assert.Equal(expected, IssuerNormalizer.Normalize(input));

    [Theory]
    [InlineData("Amazon Web Services", "amazon-web-services")]
    [InlineData("  Héllo, World! ", "hello-world")]
    [InlineData("Microsoft 365", "microsoft-365")]
    [InlineData("C++", "c-plus-plus")]
    public void CanonicalIdsAreKebabCase(string input, string expected)
        => Assert.Equal(expected, CanonicalId.FromName(input));
}
