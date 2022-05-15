using Azure.Storage.Blobs;
using System;

namespace ShaosilBot.DependencyInjection
{
    public class DataBlobProvider
    {
        private readonly static BlobServiceClient _serviceProvider = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
        private readonly static BlobContainerClient _dataBlobContainer = _serviceProvider.GetBlobContainerClient("data");

        public static string RandomCatFact
        {
            get
            {
                var _catFacts = _dataBlobContainer.GetBlobClient("CatFacts.txt").DownloadContent().Value.Content.ToString().Split(Environment.NewLine);
                return _catFacts[Random.Shared.Next(_catFacts.Length)];
            }
        }

        public static BlobServiceClient GetBlobProvider(IServiceProvider provider)
        {
            return _serviceProvider;
        }
    }
}