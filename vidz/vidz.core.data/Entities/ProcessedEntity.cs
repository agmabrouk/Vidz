using Microsoft.WindowsAzure.Storage.Table;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace vidz.core.data.Entities
{
    [Serializable]
    public class ProcessedEntity : TableEntity
    {
        public ProcessedEntity() { }
        public ProcessedEntity(string uploadId, string userId)
        {
            this.PartitionKey = userId; this.PartitionKey = uploadId;
        }
        bool isSuccessful { get; set; }
        object fffmpegOutput { get; set; }
        string? errorMessage { get; set; }
        string uploadId { get; set; }
        string userId { get; set; }
        string fileName { get; set; }
        string resolution { get; set; }
        DateTime processDate { get; set; }
    }
}
