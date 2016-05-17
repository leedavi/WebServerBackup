using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Net.Mail;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;

namespace WebServerBackUp
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                BackupDB();
                BackupWebSites();
                IISSettings();
                IISlogFiles();
                CopyToAzure();
                ResultFile("OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                ResultFile(ex.ToString());
            }
        }


        static String GetSetting(string key)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                string result = appSettings[key] ?? "";
                return result;
            }
            catch (ConfigurationErrorsException)
            {
                Console.WriteLine("Error reading app settings");
                return "";
            }
        }

        private static void IISlogFiles()
        {
            var clearIISLogFiles = GetSetting("ClearIISLogFiles");
            if (clearIISLogFiles.ToLower() == "true")
            {
                Console.WriteLine("IIS Log Files");
                var logFileFolder = GetSetting("LogFileFolder");
                var logFileRetensionDays = GetSetting("LogFileRetensionDays");
                ExecuteCommandSync("forfiles /p \"" + logFileFolder + "\" /s /m *.* /c \"cmd /c Del @path\" /d -" + logFileRetensionDays);
            }
        }

        private static void IISSettings()
        {
            var iisSettingsBackup = GetSetting("IISSettingsBackup");            
            var webZipFolder = GetSetting("WebZipFolder");
            if (webZipFolder != "" && iisSettingsBackup.ToLower() == "true")
            {
                Console.WriteLine("IIS Settings");
                if (File.Exists(webZipFolder + "\\iis_settings.zip"))
                {
                    File.Delete(webZipFolder + "\\iis_settings.zip");
                } 

                ExecuteCommandSync("%windir%\\system32\\inetsrv\\appcmd list apppool/config/xml > " + webZipFolder + "\\iis_apppools.xml");
                ExecuteCommandSync("%windir%\\system32\\inetsrv\\appcmd list site/config/xml > " + webZipFolder + "\\iis_sites.xml");

                ZipArchive zip = ZipFile.Open(webZipFolder + "\\iis_settings.zip", ZipArchiveMode.Create);
                var file = webZipFolder + "\\iis_apppools.xml";
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                File.Delete(file);
                file = webZipFolder + "\\iis_sites.xml";
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                File.Delete(file);

                zip.Dispose();
            }
        }

        static void BackupDB()
        {
            var dataBaseBackup = GetSetting("DataBaseBackup");
            if (dataBaseBackup.ToLower() == "true")
            {
                Console.WriteLine("DB Backup");
                var sqlServer = GetSetting("SqlServer");
                var sqlAdmin = GetSetting("SqlAdmin");
                var sqlAdminPass = GetSetting("SqlAdminPass");
                var backupDBFolder = GetSetting("BackupDBFolder");
                var backupDBZipPathName = GetSetting("BackupDBZipPathName");
                var compressDBBackup = GetSetting("CompressDBBackup");

                // remove any existing backup files.
                var files1 = System.IO.Directory.GetFiles(backupDBFolder, "*.bak");
                foreach (string file in files1)
                {
                    File.Delete(file);
                }

                Server myServer = new Server(sqlServer);

                myServer.ConnectionContext.LoginSecure = false;
                myServer.ConnectionContext.Login = sqlAdmin;
                myServer.ConnectionContext.Password = sqlAdminPass;

                myServer.ConnectionContext.Connect();

                foreach (Database myDatabase in myServer.Databases)
                {
                    if (myDatabase.ID > 4 && myDatabase.Status == DatabaseStatus.Normal)
                    {
                        Backup bkpDBFull = new Backup();
                        /* Specify whether you want to back up database or files or log */
                        bkpDBFull.Action = BackupActionType.Database;
                        bkpDBFull.Database = myDatabase.Name;
                        bkpDBFull.Devices.AddDevice(backupDBFolder.TrimEnd('\\') + "\\" + myDatabase.Name + ".bak", DeviceType.File);

                        bkpDBFull.SqlBackup(myServer);
                    }
                }


                if (myServer.ConnectionContext.IsOpen) myServer.ConnectionContext.Disconnect();

                if (compressDBBackup.ToLower() == "true" && backupDBZipPathName.ToLower().EndsWith(".zip"))
                {
                    File.Delete(backupDBZipPathName);

                    // zip .bak files.
                    var files = System.IO.Directory.GetFiles(backupDBFolder, "*.bak");
                    ZipArchive zip = ZipFile.Open(backupDBZipPathName, ZipArchiveMode.Create);
                    foreach (string file in files)
                    {
                        zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                        File.Delete(file);
                    }
                    zip.Dispose();
                }
            }

        }

        static void BackupWebSites()
        {
            var websiteBackup = GetSetting("WebsiteBackup");
            if (websiteBackup.ToLower() == "true")
            {
                Console.WriteLine("Website Backup");
                var webRootFolder = GetSetting("WebRootFolder");
                var webZipFolder = GetSetting("WebZipFolder");

                // remove any existing backup files.
                var files1 = System.IO.Directory.GetFiles(webZipFolder, "*.zip");
                foreach (string file in files1)
                {
                    File.Delete(file);
                }

                if (webZipFolder != "" && webRootFolder != "")
                {
                    var lp = 1;
                    var websitelist = GetListOfWebsite(webRootFolder, new List<string>());
                    foreach (var webfolder in websitelist)
                    {
                        Console.WriteLine(webfolder);

                        var parentfolder = "";
                        var zipfolders = webfolder.Split('\\');
                        if (zipfolders.Count() > 2) parentfolder = zipfolders[zipfolders.Count() - 2] + "_";
                        var zipfilename = webZipFolder  + "\\" + parentfolder + webfolder.Split('\\').Last() + lp + ".zip";
                        ZipArchive zip = ZipFile.Open(zipfilename, ZipArchiveMode.Create);
                        zip = ZipFilesInDirRecusive(webfolder, "", zip);
                        zip.Dispose();
                        lp += 1;
                    }

                }
            }

        }

        private static ZipArchive ZipFilesInDirRecusive(String searchFolderPath,String ZipFolderPath, ZipArchive zip)
        {
            var filelist = Directory.GetFiles(searchFolderPath, "*.*");
            if (filelist.Any())
            {
                foreach (var f in filelist)
                {
                    try
                    {
                        zip.CreateEntryFromFile(f, ZipFolderPath + Path.GetFileName(f), CompressionLevel.Optimal);
                    }
                    catch (Exception ex)
                    {
                        // Ignore file in error, files may be locked.
                        // there is always the Search write.lock file which throws a lock error.
                        Console.WriteLine("ERROR: " + f);
                    }
                }
            }

            var dirlist = Directory.GetDirectories(searchFolderPath);
            foreach (var d in dirlist)
            {
                zip = ZipFilesInDirRecusive(d, ZipFolderPath + d.Split('\\').Last() + "\\", zip);
            }

            return zip;
        }


        private static List<String> GetListOfWebsite(String searchFolderPath, List<String> websitelist)
        {
            var filelist = Directory.GetFiles(searchFolderPath, "*.aspx");
            if (filelist.Any())
            {
                foreach (var f in filelist)
                {
                    if (f.EndsWith("Default.aspx"))
                    {
                        websitelist.Add(searchFolderPath);
                        return websitelist;
                    }
                }
            }

            var dirlist = Directory.GetDirectories(searchFolderPath);
            foreach (var d in dirlist)
            {
                websitelist = GetListOfWebsite(d, websitelist);
            }

            return websitelist;
        }

        private static void CopyToAzure()
        {
            var copytoazure = GetSetting("copytoazure");
            if (copytoazure.ToLower() == "true")
            {
                Console.WriteLine("Copy to Azure");

                var storageConnectionString = GetSetting("StorageConnectionString");
                var storageContainer = GetSetting("StorageContainer");

                // Retrieve storage account from connection string.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

                // Create the blob client.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();


                // Retrieve reference to a previously created container.
                CloudBlobContainer container = blobClient.GetContainerReference(storageContainer);

                // Create the container if it doesn't already exist.
                container.CreateIfNotExists();


                // copy DB backup file to webfolder so it's also copied to Azure
                var compressDBBackup = GetSetting("CompressDBBackup");
                var webZipFolder = GetSetting("WebZipFolder");
                if (compressDBBackup.ToLower() == "true")
                {
                    var backupDBZipPathName =  GetSetting("BackupDBZipPathName");
                    var copiedfilenamepath = webZipFolder.Trim('\\') + "\\" + Path.GetFileName(backupDBZipPathName);
                    if (copiedfilenamepath != backupDBZipPathName)
                    {
                        if (File.Exists(copiedfilenamepath)) File.Delete(copiedfilenamepath);
                        File.Copy(backupDBZipPathName, copiedfilenamepath);
                    }
                }

                    var retensionminutes = GetSetting("retensionminutes");
                    var filelist = Directory.GetFiles(webZipFolder, "*.zip");
                    if (filelist.Any())
                    {
                        foreach (var f in filelist)
                        {
                            Console.WriteLine("Copy Azure: " + DateTime.UtcNow + " " + f);
                            // Retrieve reference to a blob named "myblob".
                            CloudBlockBlob blockBlob = container.GetBlockBlobReference(Path.GetFileName(f));

                            // Create or overwrite the "myblob" blob with contents from a local file.
                            using (var fileStream = System.IO.File.OpenRead(f))
                            {
                                blockBlob.UploadFromStream(fileStream);
                            }

                            blockBlob.CreateSnapshot();

                        }

                        // purge snapshots
                        var bloblist = container.ListBlobs(null, true, BlobListingDetails.Snapshots);
                    foreach (IListBlobItem item in bloblist)
                    {
                        //you must cast this as a CloudBlockBlob 
                        //  because blobItem does not expose all of the properties
                        CloudBlockBlob theBlob = item as CloudBlockBlob;

                        //Call FetchAttributes so it retrieves the metadata.
                        theBlob.FetchAttributes();

                        if (theBlob.IsSnapshot)
                        {
                            if (theBlob.SnapshotTime.Value.AddMinutes(Convert.ToInt32(retensionminutes)) < DateTime.UtcNow)
                            {
                                if (theBlob.SnapshotTime.Value.Day == 1)
                                {
                                    if (theBlob.SnapshotTime.Value.AddDays(32) < DateTime.UtcNow)
                                    {
                                        theBlob.Delete();
                                    }
                                }
                                else
                                {
                                    theBlob.Delete();
                                }

                            }

                        }
                    }

                }
            }
        }


        private static void ResultFile(String resultmsg)
        {
            var webZipFolder = GetSetting("WebZipFolder");
            var copytoazure = GetSetting("copytoazure");
            if (copytoazure.ToLower() == "true" && webZipFolder != "")
            {
                Console.WriteLine("Send Result File");

                var resultfile = webZipFolder.Trim('\\') + "\\resultfile.txt";
                if (File.Exists(resultfile)) File.Delete(resultfile);
                File.WriteAllText(resultfile, resultmsg);


                var storageConnectionString = GetSetting("StorageConnectionString");
                var storageContainer = GetSetting("StorageContainer");

                // Retrieve storage account from connection string.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

                // Create the blob client.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();


                // Retrieve reference to a previously created container.
                CloudBlobContainer container = blobClient.GetContainerReference(storageContainer);

                // Create the container if it doesn't already exist.
                container.CreateIfNotExists();


                Console.WriteLine("Azure: " + DateTime.UtcNow + " " + resultfile);
                // Retrieve reference to a blob named "myblob".
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(Path.GetFileName(resultfile));

                // Create or overwrite the "myblob" blob with contents from a local file.
                using (var fileStream = System.IO.File.OpenRead(resultfile))
                {
                    blockBlob.UploadFromStream(fileStream);
                }
            }
        }



        /// <span class="code-SummaryComment"><summary></span>
        /// Executes a shell command synchronously.
        /// <span class="code-SummaryComment"></summary></span>
        /// <span class="code-SummaryComment"><param name="command">string command</param></span>
        /// <span class="code-SummaryComment"><returns>string, as output of the command.</returns></span>
        private static void ExecuteCommandSync(object command)
        {
                // create the ProcessStartInfo using "cmd" as the program to be run,
                // and "/c " as the parameters.
                // Incidentally, /c tells cmd that we want it to execute the command that follows,
                // and then exit.
                System.Diagnostics.ProcessStartInfo procStartInfo =
                    new System.Diagnostics.ProcessStartInfo("cmd", "/c " + command);

                // The following commands are needed to redirect the standard output.
                // This means that it will be redirected to the Process.StandardOutput StreamReader.
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.UseShellExecute = false;
                // Do not create the black window.
                procStartInfo.CreateNoWindow = true;
                // Now we create a process, assign its ProcessStartInfo and start it
                System.Diagnostics.Process proc = new System.Diagnostics.Process();
                proc.StartInfo = procStartInfo;
                proc.Start();
                // Get the output into a string
                string result = proc.StandardOutput.ReadToEnd();
                // Display the command output.
                Console.WriteLine(result);
        }
    }
}
