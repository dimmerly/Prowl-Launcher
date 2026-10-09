using Prowl.Rosetta;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Prowl.Launcher;

public static class GitHubRepositoryHelper
{
    public static string Normalize(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (value.StartsWith(Constants.Network.GitHubUrl, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Constants.Network.GitHubUrl.Length..];
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        if (!Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+\\z") || value.Split('/')[1] is "." or "..")
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_repository"));
        }

        return value;
    }

    public static string CacheKey(string repository) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(repository).ToLowerInvariant())))[..16].ToLowerInvariant();
}
