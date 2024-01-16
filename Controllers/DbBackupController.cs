using BackupDBWeb.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace BackupDBWeb.Controllers
{

    public class DbBackupController : Controller
    {
        private readonly AppDbContextClass _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DbBackupController(AppDbContextClass dbContext, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }
        public IActionResult Index()
        {
            List<DatabaseSelectionModel> AllDb = GetDatabaseNames();
            return View(AllDb);
        }
        public IActionResult Home()
        {
            var model = GetDatabaseNames();

            // Filter the model to include only selected databases
            var selectedDatabases = model
                .Where(db => !string.IsNullOrEmpty(_httpContextAccessor.HttpContext.Session.GetString($"{db.DatabaseNames}_Url")))
                .ToList();

            return View(selectedDatabases);
        }
        [HttpPost]
        public ActionResult BackupAndUpload(List<DatabaseSelectionModel> selectedDatabases)
        {
            if (!selectedDatabases.Any(db => db.IsDatabaseSelected))
            {
                ModelState.AddModelError(string.Empty, "Please select at least one database.");
                var updatedModel = GetDatabaseNames();
                return View("Index", updatedModel);
            }

            var backupFolderPath = "E:\\Test";
            var successMessages = new List<string>();

            foreach (var database in selectedDatabases)
            {
                if (database.IsDatabaseSelected)
                {
                    if (database.DatabaseNames == "tempdb")
                    {
                        TempData["TempDb"] = database.DatabaseNames;
                       // var updatedModel = GetDatabaseNames();
                        //return View("Index", updatedModel);
                    }
                    else
                    {
                        var databaseBackupPath = Path.Combine(backupFolderPath, $"{database.DatabaseNames}_Backup.bak");
                        BackupDatabase(database.DatabaseNames, databaseBackupPath);

                        string zipFilePath = Path.Combine(backupFolderPath, $"{database.DatabaseNames}_Backup.zip");

                        using (var zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                        {
                            zipArchive.CreateEntryFromFile(databaseBackupPath, Path.GetFileName(databaseBackupPath), CompressionLevel.Fastest);
                        }

                        string folderId = "1Z4TE52tT9bSQ3raiOnURR30Zpyy2AbTX";
                        string driveLink = UploadBackupToGoogleDrive(zipFilePath, folderId);

                        successMessages.Add($"Backup for <strong>{database.DatabaseNames}</strong> uploaded to Google Drive. Link: <strong>{driveLink}</strong>");
                        System.IO.File.Delete(databaseBackupPath);
                        System.IO.File.Delete(zipFilePath);

                        _httpContextAccessor.HttpContext.Session.SetString($"{database.DatabaseNames}_Url", driveLink);
                    }
                }
            }
              return RedirectToAction("Home");
        }
        [HttpPost]
        public ActionResult DeleteAllBackups()
        {
            try
            {
                // Replace 'YOUR_FOLDER_ID' with the actual folder ID from which you want to delete backups
                string folderIdToDelete = "1Z4TE52tT9bSQ3raiOnURR30Zpyy2AbTX";

                var credential = GetCredential();
                var service = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Google Drive API",
                });

                // List all files in the folder
                var fileListRequest = service.Files.List();
                fileListRequest.Q = $"'{folderIdToDelete}' in parents";
                var files = fileListRequest.Execute().Files;

                // Delete each file in the folder
                foreach (var file in files)
                {
                    service.Files.Delete(file.Id).Execute();
                    Console.WriteLine($"Deleted file: {file.Name}, Id: {file.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting backups from Google Drive folder: {ex.Message}");
                TempData["DeleteStatus"] = "Error deleting backups. Check the console for details.";
            }

            return RedirectToAction("Index");
        }


        public List<DatabaseSelectionModel> GetDatabaseNames()
        {
            var databaseNames = new List<DatabaseSelectionModel>();

            using (var connection = new SqlConnection(_dbContext.Database.GetDbConnection().ConnectionString))
            {
                connection.Open();

                using (var command = new SqlCommand("SELECT name FROM sys.databases", connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            databaseNames.Add(new DatabaseSelectionModel { DatabaseNames = reader["name"].ToString() });
                        }
                    }
                }
            }

            return databaseNames;
        }

        private void BackupDatabase(string databaseName, string backupPath)
        {
            try
            {
                using (var connection = new SqlConnection(_dbContext.Database.GetDbConnection().ConnectionString))
                {
                    connection.Open();

                    using (var command = new SqlCommand($"BACKUP DATABASE [{databaseName}] TO DISK = '{backupPath}'", connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

               

                Console.WriteLine($"Backup completed for database: {databaseName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error backing up database {databaseName}: {ex.Message}");
            }
        }

        private string UploadBackupToGoogleDrive(string backupFilePath, string folderId)
        {
            try
            {  
                var credential =  GetCredential();
                var service = new DriveService(new BaseClientService.Initializer()
                {
                   HttpClientInitializer = credential,
                    ApplicationName = "Google Drive API",
                });

                // Convert file to zip and upload
                string driveLink = UploadFileAsync(service, backupFilePath, folderId).GetAwaiter().GetResult();

                Console.WriteLine("Backup uploaded to Google Drive.");
                return driveLink;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading backup to Google Drive: {ex.Message}");
                return "error";
            }
        }

        private GoogleCredential GetCredential()
        {
            var filePath = "./client_secret.json";
            var credentials = GoogleCredential.FromFile(filePath).CreateScoped(DriveService.ScopeConstants.Drive);
            return credentials;
            //using (var stream = new FileStream("client_secret.json", FileMode.Open, FileAccess.Read))
            //{
            //    string credPath = "token.json";
            //    return GoogleWebAuthorizationBroker.AuthorizeAsync(
            //        GoogleClientSecrets.Load(stream).Secrets,
            //        new[] { DriveService.Scope.DriveFile },
            //        "user",
            //        CancellationToken.None,
            //        new FileDataStore(credPath, true)).Result;
            //}
        }

        private async Task<string> UploadFileAsync(DriveService service, string filePath, string folderId)
        {
            try
            {
                // Upload the zip file to Google Drive
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = Path.GetFileName(filePath),
                        Parents = new[] { folderId },
                    };

                    var request = service.Files.Create(fileMetadata, stream, "application/zip");

                    // Check the response after the upload is complete
                    var response = await request.UploadAsync();
                    if (response.Status == Google.Apis.Upload.UploadStatus.Failed)
                    {
                        Console.WriteLine($"Error response during file upload: {response.Exception.Message}");
                        return "error";
                    }

                    // Get the uploaded file information using the file Id directly
                    var fileId = request.ResponseBody?.Id;
                    if (fileId == null)
                    {
                        Console.WriteLine("Error: File Id is null.");
                        return "error";
                    }

                    // Get the file details
                    var file = await service.Files.Get(fileId).ExecuteAsync();

                    Console.WriteLine($"File uploaded to Google Drive. Id: {file.Id}");

                    // Dispose of resources
                    stream.Close();
                    System.IO.File.Delete(filePath);

                    return $"https://drive.google.com/open?id={file.Id}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading file to Google Drive: {ex.Message}");
                return "error";
            }

        }
    }
}
