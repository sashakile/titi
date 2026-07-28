using System;

namespace Orion.Auth;

/// <summary>Auth service exercised by tests for coverage attribution.</summary>
public static class AuthService
{
    public static bool ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return false;
        return username.Length >= 3 && password.Length >= 8;
    }

    public static string IssueToken(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("username required", nameof(username));
        return $"tok-{username}-{username.Length}";
    }
}
