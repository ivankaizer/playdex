using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Api.Modules.Posters;

public sealed record StoredPoster(byte[] Content, string ContentType);

public interface IPosterStore
{
    Task<StoredPoster?> Find(string key, CancellationToken ct);
    Task Save(string key, StoredPoster poster, CancellationToken ct);
}

public sealed class R2PosterStore : IPosterStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public R2PosterStore(IOptions<PostersOptions> options)
    {
        var r2 = options.Value.R2;
        _bucket = r2.Bucket!;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = $"https://{r2.AccountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto",
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task<StoredPoster?> Find(string key, CancellationToken ct)
    {
        try
        {
            using var response = await _client.GetObjectAsync(_bucket, key, ct);
            using var content = new MemoryStream();
            await response.ResponseStream.CopyToAsync(content, ct);
            return new StoredPoster(content.ToArray(), response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task Save(string key, StoredPoster poster, CancellationToken ct)
    {
        using var content = new MemoryStream(poster.Content);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = poster.ContentType,
            // R2 does not support streaming SigV4 payload signing.
            DisablePayloadSigning = true,
        }, ct);
    }

    public void Dispose() => _client.Dispose();
}
