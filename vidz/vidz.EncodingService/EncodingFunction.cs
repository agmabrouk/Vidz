using System;
using System.Diagnostics;
using System.IO;

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using vidz.core.data.Entities;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.Table;

namespace vidz.EncodingService
{
    public class EncodingFunction
    {
        [FunctionName("EncodeVideo")]
        public static void EncodeVideo([QueueTrigger("newvidz-queue", Connection = "StorageAccountConnectionString")] string myQueueItem, ILogger log, ExecutionContext context)
        {
            var devEnvironmentVariable = Environment.GetEnvironmentVariable("ENVIRONMENT_TYPE");
            var isDevelopment = string.IsNullOrEmpty(devEnvironmentVariable) || devEnvironmentVariable.ToLower() == "development";

            var builder = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            if (isDevelopment)
                builder.AddUserSecrets(typeof(EncodingFunction).Assembly);

            var config = builder.Build();
            //Storage Account 
            var storageAccount = CloudStorageAccount.Parse(config["StorageAccountConnectionString"]);
            //Message has reached MaxDequeueCount of 5. Moving message to queue
            var messageObj = JsonConvert.DeserializeObject<UploadEntity>(myQueueItem);

            string output = string.Empty;
            bool isSuccessful = true;
            dynamic ffmpegResult = new JObject();
            string errorText = string.Empty;
            int exitCode = 0;

            log.LogInformation("C# HTTP trigger function processed a request.");

            var ffmpegArguments = " -i {input} -vf scale={resolution}:-2  -vcodec libx264 -crf 30 {output} -y";
            var sasInputUrl = messageObj.DownloadUrl;
            var sasOutputUrl = string.Empty;

            log.LogInformation("Arguments : ");
            log.LogInformation(ffmpegArguments);

            try
            {
                var folder = context.FunctionDirectory;
                var tempFolder = Path.GetTempPath();
                string targetResolution = messageObj.Resolution ?? "320";

                string inputFileName = $"{targetResolution}_{System.IO.Path.GetFileName(new Uri(sasInputUrl).LocalPath)}";
                string pathLocalInput = System.IO.Path.Combine(tempFolder, inputFileName);
                
                string outputFileName = $"{targetResolution}p_{messageObj.FileName}";
                string pathLocalOutput = System.IO.Path.Combine(tempFolder, outputFileName);

                foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    log.LogInformation($"{drive.Name}: {drive.TotalFreeSpace / 1024 / 1024} MB");
                }

                /* Downloads the original video file from blob to local storage. */
                log.LogInformation("Dowloading source file from blob to local");
                using (FileStream fs = System.IO.File.Create(pathLocalInput))
                {
                    try
                    {
                        var readBlob = new CloudBlob(new Uri(sasInputUrl));
                        readBlob.DownloadToStreamAsync(fs).GetAwaiter().GetResult();
                        log.LogInformation("Downloaded input file from blob");
                    }
                    catch (Exception ex)
                    {
                        log.LogError("There was a problem downloading input file from blob. " + ex.ToString());
                    }
                }

                log.LogInformation("Encoding...");
                var file = System.IO.Path.Combine(folder, "..\\ffmpeg\\ffmpeg.exe");

                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.FileName = file;

                process.StartInfo.Arguments = ffmpegArguments
                    .Replace("{input}", "\"" + pathLocalInput + "\"")
                    .Replace("{output}", "\"" + pathLocalOutput + "\"")
                    .Replace("{resolution}", "\"" + targetResolution + "\"")
                    .Replace("'", "\"");

                log.LogInformation(process.StartInfo.Arguments);

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.OutputDataReceived += new DataReceivedEventHandler(
                    (s, e) =>
                    {
                        log.LogInformation("O: " + e.Data);
                    }
                );
                process.ErrorDataReceived += new DataReceivedEventHandler(
                    (s, e) =>
                    {
                        log.LogInformation("E: " + e.Data);
                    }
                );
                //start process
                process.Start();
                log.LogInformation("process started");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                exitCode = process.ExitCode;
                ffmpegResult = output;

                log.LogInformation("Video Converted");


                //Blob Setup
                var blobClient = storageAccount.CreateCloudBlobClient();
                var container = blobClient.GetContainerReference(config["ContainerName"]);
                container.CreateIfNotExistsAsync().GetAwaiter().GetResult();
                var blobPath = $"{messageObj.User}/{messageObj.UploadId}/Processed/{outputFileName}";
                var blob = container.GetBlockBlobReference(blobPath);

                /* Uploads the encoded video file from local to blob. */
                log.LogInformation("Uploading encoded file to blob");
                using (FileStream fs = System.IO.File.OpenRead(pathLocalOutput))
                {
                    try
                    {
                        blob.UploadFromStreamAsync(fs).Wait();
                        log.LogInformation("Uploaded encoded file to blob");
                    }
                    catch (Exception ex)
                    {
                        log.LogInformation("Upload Process Failed. " + ex.ToString());
                    }
                }
                System.IO.File.Delete(pathLocalInput);
                System.IO.File.Delete(pathLocalOutput);
            }
            catch (Exception e)
            {
                isSuccessful = false;
                errorText += e.Message;
            }

            if (exitCode != 0)
            {
                isSuccessful = false;
            }

            var response = new JObject
            {
                {"isSuccessful", isSuccessful},
                {"ffmpegResult",  ffmpegResult},
                {"errorText", errorText }

            };
        }
    }
}
