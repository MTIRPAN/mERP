using System.Diagnostics;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Merp.Testing.Containers;

/// <summary>
/// SeaweedFS (Apache-2.0) with its S3 gateway, the S3 compatible store of the file storage tests (D16, D31). MinIO is
/// not used (D19). Without an identity configuration the gateway accepts any key pair.
/// </summary>
public sealed class SeaweedFsFixture : IAsyncLifetime
{
    public const string Bucket = "merp-test";
    public const string AccessKey = "test";
    public const string SecretKey = "test-secret";
    private const int S3Port = 8333;

    private readonly IContainer _container = new ContainerBuilder("chrislusf/seaweedfs:4.47")
        .WithCommand("server", "-s3", $"-s3.port={S3Port}", "-dir=/data", "-volume.max=10")
        .WithPortBinding(S3Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
            .ForPort(S3Port)
            .ForPath("/")
            .ForStatusCodeMatching(status => (int)status < 500)))
        .Build();

    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(S3Port)}";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // The gateway answers before the volume servers can take writes: retry until a bucket and an object work
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(AccessKey, SecretKey),
            new AmazonS3Config
            {
                ServiceURL = ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
        await RetryAsync(async () =>
        {
            try
            {
                await client.PutBucketAsync(Bucket);
            }
            catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
                // Created by an earlier attempt
            }
        });
        await RetryAsync(async () =>
        {
            await client.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = "ready", ContentBody = "ready" });
            await client.DeleteObjectAsync(Bucket, "ready");
        });
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static async Task RetryAsync(Func<Task> action)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception) when (elapsed.Elapsed < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }
}
