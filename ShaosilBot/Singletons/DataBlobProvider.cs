using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
    public class DataBlobProvider : IDataBlobProvider
    {
        private readonly ILogger<DataBlobProvider> _logger;
        private readonly BlobServiceClient _serviceClient;
        private readonly BlobContainerClient _dataBlobContainer;
        private readonly Dictionary<string, string> fileLeases = new Dictionary<string, string>();

        public DataBlobProvider(ILogger<DataBlobProvider> logger)
        {
            _logger = logger;
            _serviceClient = new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
            _dataBlobContainer = _serviceClient.GetBlobContainerClient("data");
        }

        public async Task<string> GetBlobTextAsync(string filename, bool aquireLease = false)
        {
            _logger.LogInformation($"Getting '{filename}' from data blob");
            var blobClient = _dataBlobContainer.GetBlobClient(filename);

            // Optional pessimistic concurrency with infinite lease - retry until lease aquired, then store the lease ID for this file
            BlobLease lease = null;
            if (aquireLease)
            {
                _logger.LogInformation($"Aquiring lease");
                var leaseClient = blobClient.GetBlobLeaseClient();
                while (string.IsNullOrEmpty(lease?.LeaseId))
                {
                    try
                    {
                        lease = await leaseClient.AcquireAsync(BlobLeaseClient.InfiniteLeaseDuration);
                        fileLeases[filename] = lease.LeaseId;
                    }
                    catch (RequestFailedException)
                    {
                        // Retry after 1/10th of a second
                        Thread.Sleep(100);
                    }
                }
            }

            var requestConditions = new BlobRequestConditions { LeaseId = lease?.LeaseId };
            return (await blobClient.DownloadContentAsync(requestConditions)).Value.Content.ToString();
        }

        public async Task SaveBlobTextAsync(string filename, string content, bool releaseLease = true)
        {
            _logger.LogInformation($"Saving '{filename}' to data blob");

            // If we have a lease ID for the specified file, use it then remove it from the dictionary if successful. No need for retries
            fileLeases.TryGetValue(filename, out var leaseId);
            var uploadOptions = new BlobUploadOptions { Conditions = new BlobRequestConditions { LeaseId = leaseId, } };

            var blobClient = _dataBlobContainer.GetBlobClient(filename);
            await blobClient.UploadAsync(new BinaryData(content), uploadOptions);

            // If lease exists, release after successfully overwriting blob
            if (releaseLease)
                ReleaseFileLease(filename);
        }

        public void ReleaseFileLease(string filename)
        {
            if (!string.IsNullOrEmpty(filename) && fileLeases.ContainsKey(filename))
            {
                _logger.LogInformation($"Releasing lease");
                var blobClient = _dataBlobContainer.GetBlobClient(filename);
                blobClient.GetBlobLeaseClient(fileLeases[filename]).Release();
                fileLeases.Remove(filename);
            }
        }
    }
}