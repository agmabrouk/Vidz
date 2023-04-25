using Microsoft.WindowsAzure.Storage.Table;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace vidz.core.data.Entities
{
    [Serializable]
    public class UploadEntity : TableEntity
    {
        public UploadEntity(string uploadId)
        {
            this.PartitionKey = "uploads";
            this.RowKey = uploadId;
        }

        public UploadEntity() { }

        public string? FileName { get; set; }

        public string? User { get; set; }

        public DateTime UploadDate { get; set; }

        public string? DownloadUrl { get; set; }

        public string? UploadUrl { get; set; }
    }
}
