using Azure.Storage.Blobs;
using System;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
    public class DataBlobProvider
    {
        private readonly BlobServiceClient _serviceClient;
        private readonly BlobContainerClient _dataBlobContainer;

        public DataBlobProvider()
        {
            _serviceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
            _dataBlobContainer = _serviceClient.GetBlobContainerClient("data");
        }

        public async Task<string> GetBlobTextAsync(string filename)
        {
            return (await _dataBlobContainer.GetBlobClient(filename).DownloadContentAsync()).Value.Content.ToString();
        }

        public async Task SaveBlobTextAsync(string filename, string content)
        {
            await _dataBlobContainer.GetBlobClient(filename).UploadAsync(new BinaryData(content), overwrite: true);
        }
    }
}