using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace EplanCipMvp.App
{
    /// <summary>
    /// 02.10.2026: заливка результата "export-all" на Google Drive.
    ///
    /// ПЕРВАЯ ВЕРСИЯ использовала сервисный аккаунт — живой прогон показал, что это
    /// в принципе не работает на обычном личном Диске (не Google Workspace):
    /// "Service Accounts do not have storage quota" — сервисный аккаунт может создавать
    /// ПАПКИ (они не расходуют квоту), но не может залить СОДЕРЖИМОЕ файла (им владеть
    /// физически негде). Нужны либо Shared Drive (только Workspace), либо OAuth от
    /// настоящего пользователя — отсюда текущая версия: OAuth "installed app" flow
    /// (GoogleWebAuthorizationBroker), токен кэшируется локально после первого входа
    /// в браузере, дальше работает без браузера. Скоуп — узкий DriveFile (доступ только
    /// к файлам, созданным самим приложением), а не полный Drive.
    /// </summary>
    public class GoogleDriveUploader
    {
        private readonly DriveService _service;
        private readonly string _folderId;

        public GoogleDriveUploader(string clientSecretPath, string tokenStoreDir, string folderId)
        {
            if (!Path.IsPathRooted(clientSecretPath))
                clientSecretPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, clientSecretPath);
            if (!File.Exists(clientSecretPath))
                throw new FileNotFoundException(
                    $"Файл OAuth-учётных данных не найден: {clientSecretPath}. " +
                    "Скачайте его в Google Cloud Console (OAuth client ID -> Desktop app) и " +
                    "положите как google-oauth-client.json рядом с exe.",
                    clientSecretPath);

            UserCredential credential;
            using (var stream = new FileStream(clientSecretPath, FileMode.Open, FileAccess.Read))
            {
                // 02.10.2026: блокирующий .Result — у конструктора нет смысла быть async
                // (вызывается один раз на запуск из синхронного Main/обработчика кнопки),
                // так же, как остальная EPLAN-обвязка в этом проекте.
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new[] { DriveService.Scope.DriveFile },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(tokenStoreDir, true)).Result;
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
                // 02.10.2026: request.Upload() для медиа-загрузки НЕ бросает исключение сам
                // по себе при сбое (resumable upload, не обычный Execute()) — нужно явно
                // проверять IUploadProgress.Status/Exception, иначе лог молча врёт об успехе
                // даже когда файл реально не долетел (так и было с сервисным аккаунтом —
                // папка создавалась, а Upload() возвращал Status=Failed без исключения).
                IUploadProgress progress = request.Upload();
                if (progress.Status != UploadStatus.Completed)
                    throw new IOException($"Не удалось загрузить '{name}': {progress.Status}" +
                        (progress.Exception != null ? $" — {progress.Exception.Message}" : ""), progress.Exception);
            }
        }
    }
}
