using Api.Infrastructure.Credentials;
using Microsoft.Extensions.Options;

namespace Api.Tests;

internal static class TestCredentials
{
    /// <summary>The key every hosted API in these tests uses (<see cref="PlaydexApiFactory"/>), so they read what tests write.</summary>
    public const string Key = "integration-test-credentials-key-at-least-thirty-two-bytes";

    public static readonly CredentialProtector Protector = Create(Key);

    public static CredentialProtector Create(string key) =>
        new(Options.Create(new CredentialOptions { EncryptionKey = key }));
}
