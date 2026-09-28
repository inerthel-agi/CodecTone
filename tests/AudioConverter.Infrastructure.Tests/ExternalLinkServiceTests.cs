using AudioConverter.Infrastructure.Shell;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class ExternalLinkServiceTests
{
    [TestMethod]
    public void GitHubProfile_UsesTheApprovedHttpsProfile()
    {
        Assert.AreEqual("https://github.com/inerthel-agi", ExternalLinkService.GitHubProfile.AbsoluteUri.TrimEnd('/'));
        Assert.IsTrue(ExternalLinkService.IsAllowedGitHubProfile(ExternalLinkService.GitHubProfile));
    }

    [DataTestMethod]
    [DataRow("http://github.com/inerthel-agi")]
    [DataRow("https://github.com/other")]
    [DataRow("https://github.com.evil.test/inerthel-agi")]
    public void IsAllowedGitHubProfile_RejectsUnapprovedUris(string value)
    {
        Assert.IsFalse(ExternalLinkService.IsAllowedGitHubProfile(new Uri(value)));
    }
}
