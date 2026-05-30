using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace RyveSwift.Api.Services;

public class SpacesStorageService
{
    private readonly ConfigService _config;

    public SpacesStorageService(ConfigService config)
    {
        _config = config;
    }

    private AmazonS3Client CreateClient()
    {
        var key      = _config.Get("SPACES_KEY");
        var secret   = _config.Get("SPACES_SECRET");
        var endpoint = _config.Get("SPACES_ENDPOINT"); // e.g. https://tor1.digitaloceanspaces.com

        var credentials = new BasicAWSCredentials(key, secret);
        var config = new AmazonS3Config
        {
            ServiceURL            = endpoint,
            ForcePathStyle        = false,
            AuthenticationRegion  = ExtractRegion(endpoint)
        };
        return new AmazonS3Client(credentials, config);
    }

    private static string ExtractRegion(string endpoint)
    {
        // https://tor1.digitaloceanspaces.com → tor1
        var host = new Uri(endpoint).Host;            // tor1.digitaloceanspaces.com
        return host.Split('.')[0];                    // tor1
    }

    public async Task UploadAsync(string key, byte[] bytes, string contentType = "application/pdf")
    {
        var bucket = _config.Get("SPACES_BUCKET");
        using var client = CreateClient();
        using var ms = new MemoryStream(bytes);

        var request = new PutObjectRequest
        {
            BucketName  = bucket,
            Key         = key,
            InputStream = ms,
            ContentType = contentType,
            // Private by default — served via API proxy
            CannedACL   = S3CannedACL.Private
        };

        await client.PutObjectAsync(request);
    }

    public async Task<byte[]> DownloadAsync(string key)
    {
        var bucket = _config.Get("SPACES_BUCKET");
        using var client = CreateClient();

        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key        = key
        };

        using var response = await client.GetObjectAsync(request);
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
