using System.Net;
using System.Runtime.CompilerServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Merp.Infrastructure.Files;

/// <summary>Files in an S3 compatible bucket (D16): AWS S3, SeaweedFS, Garage and the like. MinIO is not used (D19).</summary>
internal sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3FileStorage(IOptions<FileStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var s3 = options.Value.S3;
        if (string.IsNullOrWhiteSpace(s3.Bucket))
        {
            throw new InvalidOperationException("Files:S3:Bucket is required when Files:Storage is S3.");
        }

        var config = new AmazonS3Config
        {
            ForcePathStyle = s3.ForcePathStyle,
            // Self hosted stores do not all understand the newer flexible checksums; send them only when required
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (string.IsNullOrWhiteSpace(s3.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Region);
        }
        else
        {
            config.ServiceURL = s3.ServiceUrl;
            config.AuthenticationRegion = s3.Region;
        }

        // Credentials come only from configuration, never from the SDK's environment or profile lookup
        var credentials = new BasicAWSCredentials(
            s3.AccessKey ?? throw new InvalidOperationException("Files:S3:AccessKey is required when Files:Storage is S3."),
            s3.SecretKey ?? throw new InvalidOperationException("Files:S3:SecretKey is required when Files:Storage is S3."));
        _client = new AmazonS3Client(credentials, config);
        _bucket = s3.Bucket;
    }

    public async Task WriteAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        StorageKeys.Validate(key);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        await _client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        StorageKeys.Validate(key);
        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"The stored file '{key}' is missing.", key, exception);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        StorageKeys.Validate(key);
        await _client.DeleteObjectAsync(_bucket, key, cancellationToken);
    }

    public async IAsyncEnumerable<StorageObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pages = _client.Paginators.ListObjectsV2(new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix });
        await foreach (var item in pages.S3Objects.WithCancellation(cancellationToken))
        {
            var lastModified = item.LastModified is { } modified
                ? new DateTimeOffset(DateTime.SpecifyKind(modified.ToUniversalTime(), DateTimeKind.Utc))
                : DateTimeOffset.MinValue;
            yield return new StorageObject(item.Key, lastModified);
        }
    }

    public void Dispose() => _client.Dispose();
}
