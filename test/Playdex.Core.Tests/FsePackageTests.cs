using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class FsePackageTests
{
    /// <summary>playdex-launcher.cer as published on download.playdex.dev for v2.1.0 to v2.1.4 (public).</summary>
    private const string PublishedCertificate =
        "MIIC9jCCAd6gAwIBAgIJAJf+WDeLqkRrMA0GCSqGSIb3DQEBCwUAMB8xHTAbBgNVBAMTFFBsYXlkZXggTGF1bmNoZXIgRGV2MB4X"
        + "DTI2MTAwNzE1MjQwM1oXDTI4MTAwODE1MjQwM1owHzEdMBsGA1UEAxMUUGxheWRleCBMYXVuY2hlciBEZXYwggEiMA0GCSqGSIb3"
        + "DQEBAQUAA4IBDwAwggEKAoIBAQC/fTHXS3rxr5/14+fbomtv0TycZoQtT9ZLhgg141IX+Sc5WWPCUSvNHqeHbGFBw6bZ+YA3FyGF"
        + "4m83r9cqeQqe7UWQr5GKUrOcRJ03At2jn2hFiOCJhCC5esc3zJIe8FmCPo346CfoWnPDGwUU46s1JbOieXBYypUrRLxEeT9LPtlg"
        + "lOXB6UZD+0qGxfOYLOaF4D83Ad46mQ467E5gEuk7TLvJD+YJk6yaC4QG6Cmtu4RicLdWPSbGKUCJPNNR6QlaG/hM9GyHnOb+SBUT"
        + "+N6i8t3UjAI2ev/0xDDV3P+Uw1cmp9Vhoci33PS0jVyJfsItgFHCQcqRdfLGOObDtasFAgMBAAGjNTAzMAwGA1UdEwEB/wQCMAAw"
        + "DgYDVR0PAQH/BAQDAgeAMBMGA1UdJQQMMAoGCCsGAQUFBwMDMA0GCSqGSIb3DQEBCwUAA4IBAQBfbTReLt2ZDZiWZOm7h5c0mqjZ"
        + "T5pC2MQ9TKvhNrum9iyA3eTZ/swWeso40U3c94DnKyayueBLBqY09G69ChmodFQxUa7XtTCyNZ2nQoBE8h1cq0EDQMMP8qAZ7fz+"
        + "TPVCN2h7DC2C0jinvk4CAHEI886vg9jJfXVQQwBkuAFRLW6JuxVI9E+t1dol7J0So+k0SpPrqffUrSAJHgbajlZYpahAS+DTSCuJ"
        + "xsVIrvNfSjsZ/bT4aCRLLl44HPfTXldv8VoA/7DYL2aakNDSabPAZUs50R7cPeZlqB9sV/B2zTKJiI4PRtk6+SQQlft9k1cKI2aU"
        + "cyjw466I/X6gWJ/f";

    private readonly string _dir = NewTempDir();

    [Fact]
    public void ThePublishedCertificateIsThePinnedOne()
    {
        var path = Write("playdex-launcher.cer", Convert.FromBase64String(PublishedCertificate));

        using var certificate = FsePackage.LoadSigningCertificate(path);

        Assert.Equal(FsePackage.SigningCertificateSha256, certificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Equal("CN=Playdex Launcher Dev", certificate.Subject);
    }

    [Fact]
    public void AnotherCertificateWithTheSameNameIsNotTrusted()
    {
        using var impostor = SelfSigned("CN=Playdex Launcher Dev");
        var path = Write("playdex-launcher.cer", impostor.Export(X509ContentType.Cert));

        var refused = Assert.Throws<InvalidDataException>(() => FsePackage.LoadSigningCertificate(path));

        Assert.Contains("not Playdex's launcher signing certificate", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACertificateFileThatIsNotOneOrIsTooLargeIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => FsePackage.LoadSigningCertificate(Write("garbage.cer", "not a certificate"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => FsePackage.LoadSigningCertificate(Write("large.cer", new byte[FsePackage.MaximumCertificateBytes + 1])));
        Assert.Throws<InvalidDataException>(() => FsePackage.LoadSigningCertificate(Path.Combine(_dir, "missing.cer")));
    }

    [Theory]
    [InlineData("UnknownError")]
    [InlineData("NotTrusted")]
    [InlineData("Valid")]
    public void APackageSignedWithThePinnedCertificateIsAccepted(string status)
    {
        FsePackage.CheckPackageSignature($"{status}\r\n{PublishedCertificate}\r\n");
        // The console may wrap the long base64 line.
        FsePackage.CheckPackageSignature($"{status}\n{PublishedCertificate[..120]}\n{PublishedCertificate[120..]}\n");
    }

    [Theory]
    [InlineData("HashMismatch")]
    [InlineData("NotSigned")]
    [InlineData("NotSupportedFileFormat")]
    [InlineData("Incompatible")]
    public void APackageWhoseSignatureDoesNotHoldIsRefused(string status) =>
        Assert.Throws<InvalidDataException>(() => FsePackage.CheckPackageSignature($"{status}\n{PublishedCertificate}\n"));

    [Fact]
    public void APackageSignedWithAnotherCertificateOrNoneIsRefused()
    {
        using var impostor = SelfSigned("CN=Playdex Launcher Dev");
        var other = Convert.ToBase64String(impostor.Export(X509ContentType.Cert));

        var refused = Assert.Throws<InvalidDataException>(() => FsePackage.CheckPackageSignature($"UnknownError\n{other}\n"));
        Assert.Contains("not signed with Playdex's launcher signing certificate", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => FsePackage.CheckPackageSignature("NotSigned\n"));
        Assert.Throws<InvalidDataException>(() => FsePackage.CheckPackageSignature("UnknownError\n"));
        Assert.Throws<InvalidDataException>(() => FsePackage.CheckPackageSignature("UnknownError\nnot base64!\n"));
        Assert.Throws<InvalidDataException>(() => FsePackage.CheckPackageSignature(""));
    }

    [Fact]
    public void TrustImportsTheCheckedBytesNotTheFile()
    {
        var path = Write("playdex-launcher.cer", Convert.FromBase64String(PublishedCertificate));
        using var certificate = FsePackage.LoadSigningCertificate(path);

        var command = FsePackage.TrustCommand(certificate);

        Assert.Contains($"'{PublishedCertificate}'", command, StringComparison.Ordinal);
        Assert.Contains("'TrustedPeople', 'LocalMachine'", command, StringComparison.Ordinal);
        Assert.DoesNotContain(path, command, StringComparison.OrdinalIgnoreCase);
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static X509Certificate2 SelfSigned(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }
}
