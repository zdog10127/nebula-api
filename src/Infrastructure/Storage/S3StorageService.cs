using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DiscordClone.Application.Storage;

namespace DiscordClone.Infrastructure.Storage;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _client;
    private readonly S3Options _options;

    public S3StorageService(S3Options options)
    {
        _options = options;

        _client = new AmazonS3Client(options.AccessKey, options.SecretKey, new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = true,
        });
    }

    public async Task EnsureBucketExistsAsync(CancellationToken ct)
    {
        var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.Bucket);
        if (!exists)
            await _client.PutBucketAsync(_options.Bucket, ct);

        var policy = $$"""
        {
          "Version": "2012-10-17",
          "Statement": [
            {
              "Effect": "Allow",
              "Principal": "*",
              "Action": ["s3:GetObject"],
              "Resource": ["arn:aws:s3:::{{_options.Bucket}}/*"]
            }
          ]
        }
        """;

        await _client.PutBucketPolicyAsync(_options.Bucket, policy, ct);
    }

    public async Task UploadAsync(string key, Stream content, string contentType, string? contentDisposition, CancellationToken ct)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        if (contentDisposition is not null)
            request.Headers.ContentDisposition = contentDisposition;

        await _client.PutObjectAsync(request, ct);
    }

    public string GetPublicUrl(string key) => $"{_options.PublicEndpoint.TrimEnd('/')}/{_options.Bucket}/{key}";
}
