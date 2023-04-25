using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace vidz.EncodingService
{
    public class Function1
    {
        [FunctionName("Function1")]
        public void Run([QueueTrigger("newvidz-queue", Connection = "stgConnectionString")]string myQueueItem, ILogger log)
        {
            log.LogInformation($"C# Queue trigger function processed: {myQueueItem}");
        }
    }
}
