using System;
using System.Collections.Generic;
using System.IO;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 02.10.2026: заливка результата "export-all" на Google Drive от имени отдельного
    /// сервисного аккаунта (не личный OAuth), расшаренного только на одну целевую папку
    /// (settings.GoogleDrive.FolderId). Ключ — внешний файл, в git не попадает (см. .gitignore).
    /// Скоуп — узкий DriveFile (доступ только к файлам, созданным самим приложением/расшаренным
    /// на него), а не полный Drive.
    /// </summary>
    public class GoogleDriveUploader
    {
        private readonly DriveService _service;
        private readonly string _folderId;

        public GoogleDriveUploader(string serviceAccountKeyPath, string folderId)
        {
            if (!File.Exists(serviceAccountKeyPath))
                throw new FileNotFoundException(
                    $"Ключ сервисного аккаунта не найден: {serviceAccountKeyPath}. " +
                    "Положите drive-service-account.json рядом с exe (в git не коммитится).",
                    serviceAccountKeyPath);

            GoogleCredential credential;
            using (var stream = new FileStream(serviceAccountKeyPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(DriveService.Scope.DriveFile);
            }

            _service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "EplanCipMvp export-all",
            });
            _folderId = folderId;
        }

        /// <summary>Создаёт подпапку с датой внутри целевой папки и грузит туда все файлы
        /// из localDir — не захламляет корень при повторных запусках. Возвращает ссылку
        /// на новую подпапку.</summary>
        public string UploadFolder(string localDir, Action<string> log)
        {
            string subfolderName = $"export-{DateTime.Now:yyyy-MM-dd_HH-mm}";
            string subfolderId = CreateFolder(subfolderName, _folderId);
            log($"Создана папка на Диске: {subfolderName}");

            foreach (var path in Directory.GetFiles(localDir))
            {
                string name = Path.GetFileName(path);
                UploadFile(path, name, subfolderId);
                log($"  Загружен: {name}");
            }

            return $"https://drive.google.com/drive/folders/{subfolderId}";
        }

        private string CreateFolder(string name, string parentId)
        {
            var metadata = new DriveFile
            {
                Name = name,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId },
            };
            var request = _service.Files.Create(metadata);
            request.Fields = "id";
            var created = request.Execute();
            return created.Id;
        }

        private void UploadFile(string localPath, string name, string parentId)
        {
            var metadata = new DriveFile
            {
                Name = name,
                Parents = new List<string> { parentId },
            };
            using (var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read))
            {
                string mimeType = Path.GetExtension(localPath).ToLowerInvariant() == ".json"
                    ? "application/json"
                    : "application/octet-stream";
                var request = _service.Files.Create(metadata, stream, mimeType);
                request.Fields = "id";
                request.Upload();
            }
        }
    }
}
