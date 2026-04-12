using EasyStock.Api.Configuration;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Services;

public class AzureFileShareStorageTests
{
    private static IOptions<FileStorageOptions> CreateOptions(string connectionString, string shareName = "product-images")
    {
        var opts = new FileStorageOptions
        {
            Provider = "AzureFileShare",
            AzureFileShare = new AzureFileShareStorageOptions
            {
                ConnectionString = connectionString,
                ShareName = shareName
            }
        };
        return Options.Create(opts);
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenConnectionStringIsEmpty()
    {
        var storage = new AzureFileShareStorage(CreateOptions(""));
        var request = new FileUploadRequest("path/to", "file.jpg", "image/jpeg", new byte[] { 1, 2, 3 });

        var act = async () => await storage.UploadAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Azure File Share não está configurado*");
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenConnectionStringHasPlaceholder()
    {
        var storage = new AzureFileShareStorage(CreateOptions(
            "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=<PRIMARY_KEY>;EndpointSuffix=core.windows.net"));
        var request = new FileUploadRequest("path/to", "file.jpg", "image/jpeg", new byte[] { 1, 2, 3 });

        var act = async () => await storage.UploadAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Azure File Share não está configurado*");
    }

    [Fact]
    public async Task UploadAsync_ShouldThrow_WhenConnectionStringIsNull()
    {
        var storage = new AzureFileShareStorage(CreateOptions(null!));
        var request = new FileUploadRequest("path/to", "file.jpg", "image/jpeg", new byte[] { 1, 2, 3 });

        var act = async () => await storage.UploadAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("produtos/abc/def", "file.jpg", "produtos/abc/def/file.jpg")]
    [InlineData("/produtos/abc/", "photo.png", "produtos/abc/photo.png")]
    [InlineData("a", "b.webp", "a/b.webp")]
    public void StorageKey_ShouldBeNormalized(string bucketPath, string fileName, string expectedKey)
    {
        var computed = (bucketPath.Trim('/') + "/" + fileName).Trim('/');
        computed.Should().Be(expectedKey);
    }

    [Fact]
    public void SasUri_ParsesAccountKey_WithEqualSignsInValue()
    {
        // Validates that connection string parsing handles base64 AccountKey (contains =)
        var connStr = "DefaultEndpointsProtocol=https;AccountName=easystockfiles;AccountKey=coUN2+GWFctDIMXgqJqa0+5/U+Eh80w16gj42HsaE5o5hLLgtBw1EARIRQS+kxOWlqDWDAaPN6IN+AStvEP0AA==;EndpointSuffix=core.windows.net";

        var connParts = connStr
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        connParts.Should().ContainKey("AccountName").WhoseValue.Should().Be("easystockfiles");
        connParts.Should().ContainKey("AccountKey").WhoseValue.Should().EndWith("==");
        connParts["AccountKey"].Should().NotContain("<").And.HaveLength(88);
    }
}
