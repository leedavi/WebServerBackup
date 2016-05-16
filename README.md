# WebServerBackup
Auto Backup DNN Website files, data and copy to Azure Storage

What is WebServerBackUp
-----------------------

WebServerBackUp is a console application designed to run from the windows task manager or manually activated.
It is designed to backup all the files required by DNN Websites on the server and make a versioned (snapshot) history of these files on Azure Storage.


Functions
---------

#BackupDB();
Creates a backup of ALL user databases in a SQL server instance. And then zips these into a single zip file. 

#BackupWebSites();
Searches for any subfolder from the root folder provided, that contains a "Default.aspx" file.
All sub-folders containing a "Default.aspx" will be zipped into individual sub-folder named zip files.

#IISSettings();
Makes a copy of all IIS settings and zips them

#IISlogFiles();
Clears all IIS Logs files older than the date given.

#CopyToAzure();
Copies all the backup files to Azure and makes a snapshot.  
Any snapshots older than the given retension time will be deleted.
The retension time of snapshots made on the 1st of the month is set to 32 days.

#ResultFile();
Sends a results file to the azure container, indicating if the backup was successful, this can be checked to ensure the backup has worked.

Help Links
----------

https://azure.microsoft.com/en-us/documentation/articles/storage-dotnet-how-to-use-blobs


** Explorer to deal with snapshot downloads and management.
http://www.red-gate.com/products/azure-development/azure-explorer/
