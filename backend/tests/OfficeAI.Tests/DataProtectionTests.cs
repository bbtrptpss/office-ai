using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OfficeAI.Infrastructure;

namespace OfficeAI.Tests;

public class DataProtectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "officeai-dp-" + Guid.NewGuid().ToString("N"));
    private const string Password = "test-cert-password";

    public DataProtectionTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string NewCertificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(_dir, name + ".pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, Password));
        return path;
    }

    private IDataProtector Protector(string keys, Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        var builder = services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("OfficeAI");
        DependencyInjection.ProtectKeysAtRest(builder, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider().GetDataProtectionProvider().CreateProtector("test");
    }

    [Fact]
    public void Keys_are_encrypted_with_the_certificate_and_survive_rotation()
    {
        var keys = Directory.CreateDirectory(Path.Combine(_dir, "keys")).FullName;
        var first = NewCertificate("first");
        var secret = Protector(keys, new() { ["DataProtection:CertificatePath"] = first, ["DataProtection:CertificatePassword"] = Password }).Protect("cookie");

        var xml = File.ReadAllText(Directory.GetFiles(keys, "key-*.xml").Single());
        Assert.Contains("encryptedSecret", xml);
        Assert.DoesNotContain("<masterKey", xml);

        // Without the certificate the key cannot be read.
        Assert.ThrowsAny<CryptographicException>(() => Protector(keys, new()).Unprotect(secret));

        // After rotating to a new certificate, the old one listed as previous still opens existing keys.
        var second = NewCertificate("second");
        var rotated = Protector(keys, new()
        {
            ["DataProtection:CertificatePath"] = second, ["DataProtection:CertificatePassword"] = Password,
            ["DataProtection:PreviousCertificatePaths:0"] = first,
        });
        Assert.Equal("cookie", rotated.Unprotect(secret));
    }

    [Fact]
    public void A_missing_certificate_stops_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Protector(_dir, new() { ["DataProtection:CertificatePath"] = Path.Combine(_dir, "nope.pfx") }));
        Assert.Contains("not found", ex.Message);
    }
}
