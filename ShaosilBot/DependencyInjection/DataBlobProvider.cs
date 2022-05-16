using Azure.Storage.Blobs;
using System;
using System.Threading.Tasks;

namespace ShaosilBot.DependencyInjection
{
    public class DataBlobProvider
    {
        private readonly static BlobServiceClient _serviceProvider = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
        private readonly static BlobContainerClient _dataBlobContainer = _serviceProvider.GetBlobContainerClient("data");

        public static BlobServiceClient GetBlobProvider(IServiceProvider provider)
        {
            return _serviceProvider;
        }

        public static async Task<string> GetBlobText(string filename)
        {
            return (await _dataBlobContainer.GetBlobClient(filename).DownloadContentAsync()).Value.Content.ToString();
        }

        public static async Task SaveBlobText(string filename, string content)
        {
            await _dataBlobContainer.GetBlobClient(filename).UploadAsync(new BinaryData(content), overwrite: true);
        }
    }
}