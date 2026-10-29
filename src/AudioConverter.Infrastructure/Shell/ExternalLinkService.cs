using System.Diagnostics;

namespace AudioConverter.Infrastructure.Shell;

public static class ExternalLinkService
{
    public static Uri GitHubProfile { get; } = new("https://github.com/inerthel-agi");

    public static bool IsAllowedGitHubProfile(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.Port is -1 or 443
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath.TrimEnd('/').Equals("/inerthel-agi", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    public static void OpenGitHubProfile()
    {
        if (!IsAllowedGitHubProfile(GitHubProfile))
            throw new InvalidOperationException("The configured GitHub URL is not approved.");
        Process.Start(new ProcessStartInfo(GitHubProfile.AbsoluteUri) { UseShellExecute = true });
    }
}
