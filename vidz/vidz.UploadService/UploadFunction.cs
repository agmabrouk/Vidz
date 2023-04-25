using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using System.Linq;
using vidz.core.data.Entities;
using System.Security.Cryptography;
using Azure.Storage.Queues;
using Newtonsoft.Json;

namespace vidz.UploadService
{
    public static class UploadFunction
    {
        private static readonly RNGCryptoServiceProvider csp = new();

        public static RNGCryptoServiceProvider GetCsp()
        {
            return csp;
        }

        [FunctionName("UploadVideo")]
        public static async Task<IActionResult> UploadVideo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext context)
        {
            //X-MS-CLIENT-PRINCIPAL-NAME
            var clientPrincipalName = req.Headers["CLIENT-NAME"].FirstOrDefault();
            if (string.IsNullOrEmpty(clientPrincipalName))
                return new UnauthorizedResult();

            var devEnvironmentVariable = Environment.GetEnvironmentVariable("ENVIRONMENT_TYPE");
            var isDevelopment = string.IsNullOrEmpty(devEnvironmentVariable) || devEnvironmentVariable.ToLower() == "development";

            var builder = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            if (isDevelopment)
                builder.AddUserSecrets(typeof(UploadFunction).Assembly);

            var config = builder.Build();
            var file = req.Form.Files["File"];
            string filename = file.FileName;


            if (string.IsNullOrEmpty(filename))
                return new BadRequestObjectResult("Please pass a filename on the query string");

            var uploadId = Guid.NewGuid().ToString();
            //Storage Account 
            var storageAccount = CloudStorageAccount.Parse(config["StorageAccountConnectionString"]);

            //Blob Setup
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(config["ContainerName"]);
            container.CreateIfNotExistsAsync().GetAwaiter().GetResult();
            var blobPath = $"{clientPrincipalName}/{uploadId}/{filename}";
            var blob = container.GetBlockBlobReference(blobPath);
            var ReadSasToken = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy()
            {
                SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(3),
                Permissions = SharedAccessBlobPermissions.Read,
            });

            var WriteSasToken = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy()
            {
                SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(3),
                Permissions = SharedAccessBlobPermissions.Read,
            });

            var uploadUrl = $"{blob.Uri}{WriteSasToken}";
            var downloadUrl = $"{blob.Uri}{ReadSasToken}";
            Stream myBlob = new MemoryStream();
            myBlob = file.OpenReadStream();
            await blob.UploadFromStreamAsync(myBlob);
            myBlob.Close();

            //Push new message to the upload-videos-queue
            QueueClient queueClient = new QueueClient(config["StorageAccountConnectionString"], config["QueueName"]);
            // Create the queue
            queueClient.CreateIfNotExists();
            // Save Upload Details to Table for logging
            var tableClient = storageAccount.CreateCloudTableClient();
            CloudTable uploadsTable = tableClient.GetTableReference("uploads");
            await uploadsTable.CreateIfNotExistsAsync();
            var uploadEntity = new UploadEntity(uploadId)
            {
                FileName = filename,
                User = clientPrincipalName,
                UploadDate = DateTime.UtcNow,
                DownloadUrl = downloadUrl,
                UploadUrl = uploadUrl,
            };

            if (queueClient.Exists())
            {
                queueClient.SendMessage(JsonConvert.SerializeObject(uploadEntity));
                Console.WriteLine($"Message created: '{queueClient.Name}'");
            }
            await uploadsTable.ExecuteAsync(TableOperation.Insert(uploadEntity));
            //Return Video Download Details
            return new JsonResult(new
            {
                FileName = filename,
                DownloadUrl = downloadUrl,
                SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-1),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(3),
            });
        }
    }
}
