using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using EplanCipMvp.Core.CableNaming;
using EplanCipMvp.Core.WireNaming;

namespace EplanCipMvp.Web
{
    /// <summary>
    /// Простой HTTP-сервер поверх System.Net.HttpListener — слушает ТОЛЬКО
    /// localhost (см. план: пользователь открывает браузер исключительно с той
    /// же машины, где стоит EPLAN, поэтому аутентификация/шифрование не нужны).
    /// Раздаёт статику из wwwroot и три JSON-эндпоинта, дёргающие EplanSession.
    /// </summary>
    public class ApiServer
    {
        private readonly EplanSession _session;
        private readonly DialogService _dialogs;
        private readonly HttpListener _listener;
        private readonly string _wwwroot;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly CableRuleStore _cableRules;
        private readonly WireRuleStore _wireRules;
        private Thread _loopThread;
        private volatile bool _running;

        public ApiServer(EplanSession session, DialogService dialogs, string prefix)
        {
            _session = session;
            _dialogs = dialogs;
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            _cableRules = new CableRuleStore(EplanCipMvp.App.AppPaths.UserFile("cable-numbering-rules.json"));
            _wireRules = new WireRuleStore(EplanCipMvp.App.AppPaths.UserFile("wire-rules-v7.json"));
            // Ответ «Считать провода» на 2000+ узлов больше лимита JavaScriptSerializer по умолчанию (2 МБ).
            _json.MaxJsonLength = int.MaxValue;
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            _loopThread = new Thread(Loop) { IsBackground = true, Name = "EplanCipMvp-Http" };
            _loopThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { /* уже остановлен/не критично при выходе */ }
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch (HttpListenerException) { break; } // Stop() вызвал остановку — нормальный выход
                catch (ObjectDisposedException) { break; }

                // Каждый запрос — в своём потоке пула, EplanSession сама сериализует
                // реальные вызовы EPLAN через очередь на STA-поток.
                ThreadPool.QueueUserWorkItem(_ => HandleSafe(ctx));
            }
        }

        private void HandleSafe(HttpListenerContext ctx)
        {
            try { Handle(ctx); }
            catch (Exception ex)
            {
                DiagnosticLog.Error($"{ctx.Request.HttpMethod} {ctx.Request.Url.AbsolutePath}", ex);
                try
                {
                    WriteJson(ctx.Response, 500, new { error = $"{ex.GetType().Name}: {ex.Message}", logFile = DiagnosticLog.FilePath });
                }
                catch { /* соединение уже могло закрыться клиентом */ }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url.AbsolutePath;
            if (path.StartsWith("/api/", StringComparison.Ordinal))
                DiagnosticLog.Info($"→ {req.HttpMethod} {path}");

            // 23.09.2026: ошибки JavaScript в браузере — тоже в журнал, чтобы всё было в одном файле.
            if (req.HttpMethod == "POST" && path == "/api/client-log")
            {
                DiagnosticLog.Info("[браузер] " + ReadBody(req));
                WriteJson(ctx.Response, 200, true);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/connect")
            {
                var body = _json.Deserialize<ConnectRequest>(ReadBody(req));
                var result = _session.Connect(body);
                WriteJson(ctx.Response, 200, result);
                return;
            }

            var donorMatch = Regex.Match(path, @"^/api/groups/([^/]+)/donor-pages$");
            if (req.HttpMethod == "GET" && donorMatch.Success)
            {
                string groupKey = Uri.UnescapeDataString(donorMatch.Groups[1].Value);
                var pages = _session.GetDonorPages(groupKey);
                WriteJson(ctx.Response, 200, pages);
                return;
            }

            // 12.09.2026: задел под будущий автоподбор донор-страницы по таблице
            // параметров ПЧ (см. план eventual-humming-charm.md) — пока только
            // отдаёт то, что прочитал последний /api/connect, для предпросмотра.
            if (req.HttpMethod == "GET" && path == "/api/pump-parameters")
            {
                WriteJson(ctx.Response, 200, _session.GetPumpParameters());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/copy")
            {
                var items = _json.Deserialize<List<CopyItem>>(ReadBody(req));
                var log = _session.Copy(items);
                WriteJson(ctx.Response, 200, log);
                return;
            }

            // 11.09.2026: "Обзор..." — браузер не может отдать JS реальный путь к
            // файлу на диске, а сервер и браузер физически на одной машине (см.
            // план), поэтому сервер сам открывает системный диалог Windows и
            // возвращает выбранный путь. См. DialogService.cs.
            if (req.HttpMethod == "GET" && path == "/api/browse/file")
            {
                string filter = req.QueryString["filter"];
                var selected = _dialogs.BrowseFile(filter);
                WriteJson(ctx.Response, 200, selected);
                return;
            }

            if (req.HttpMethod == "GET" && path == "/api/browse/folder")
            {
                var selected = _dialogs.BrowseFolder();
                WriteJson(ctx.Response, 200, selected);
                return;
            }

            // 12.09.2026: для поля "Целевой проект", когда он создаётся как полная
            // копия донора (см. план eventual-humming-charm.md) — путь ещё не
            // существует, нужен SaveFileDialog, а не обычный "открыть файл".
            if (req.HttpMethod == "GET" && path == "/api/browse/save-file")
            {
                string filter = req.QueryString["filter"];
                var selected = _dialogs.BrowseSaveFile(filter);
                WriteJson(ctx.Response, 200, selected);
                return;
            }

            // 12.09.2026: реконсиляция после полного копирования донора (см. план) —
            // preview ничего не меняет в проекте, apply — необратимо (дублирует/
            // удаляет/переименовывает страницы), поэтому раздельные эндпоинты.
            if (req.HttpMethod == "POST" && path == "/api/reconcile/preview")
            {
                var body = _json.Deserialize<ReconcileRequest>(ReadBody(req));
                var plans = _session.ReconcilePreview(body);
                WriteJson(ctx.Response, 200, plans);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/reconcile/apply")
            {
                var body = _json.Deserialize<ReconcileRequest>(ReadBody(req));
                var log = _session.ReconcileApply(body);
                WriteJson(ctx.Response, 200, log);
                return;
            }

            // 23.09.2026: нумерация кабелей по правилам (см. docs/superpowers/specs/2026-09-23-cable-numbering-design.md).
            if (req.HttpMethod == "GET" && path == "/api/cables/rules")
            {
                WriteJson(ctx.Response, 200, _cableRules.Load());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/rules")
            {
                var rules = _json.Deserialize<List<CableRule>>(ReadBody(req));
                WriteJson(ctx.Response, 200, _cableRules.Save(rules));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/rules/reset")
            {
                WriteJson(ctx.Response, 200, _cableRules.ResetToPreset());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/read")
            {
                WriteJson(ctx.Response, 200, _session.ReadCables(_cableRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/preview")
            {
                WriteJson(ctx.Response, 200, _session.PreviewCables(_cableRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/cables/apply")
            {
                var items = _json.Deserialize<List<CableApplyItem>>(ReadBody(req));
                WriteJson(ctx.Response, 200, _session.ApplyCableNames(items));
                return;
            }

            // 24.09.2026: «Сохранить выгрузку…» — TSV последнего чтения кабелей/проводов.
            if (req.HttpMethod == "GET" && (path == "/api/cables/export" || path == "/api/wires/export"))
            {
                bool cables = path == "/api/cables/export";
                string tsv = cables ? _session.ExportCables() : _session.ExportWires();
                if (string.IsNullOrEmpty(tsv))
                {
                    WriteJson(ctx.Response, 409, new { Error = "Нечего выгружать — сначала нажмите «Считать»." });
                    return;
                }
                WriteText(ctx.Response, tsv, cables ? "export-cables.tsv" : "export-wires.tsv");
                return;
            }

            // 24.09.2026: нумерация жил и проводов (docs/superpowers/specs/2026-09-24-wire-core-numbering-design.md).
            if (req.HttpMethod == "GET" && path == "/api/wires/rules")
            {
                WriteJson(ctx.Response, 200, _wireRules.Load());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/rules")
            {
                var rules = _json.Deserialize<List<WireRule>>(ReadBody(req));
                WriteJson(ctx.Response, 200, _wireRules.Save(rules));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/rules/reset")
            {
                WriteJson(ctx.Response, 200, _wireRules.ResetToPreset());
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/read")
            {
                WriteJson(ctx.Response, 200, _session.ReadWires(_wireRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/preview")
            {
                WriteJson(ctx.Response, 200, _session.PreviewWires(_wireRules.Load()));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/restore")
            {
                WriteJson(ctx.Response, 200, _session.RestoreWires(ReadBody(req)));
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/wires/apply")
            {
                var body = _json.Deserialize<WireApplyRequest>(ReadBody(req));
                WriteJson(ctx.Response, 200, _session.ApplyWires(body));
                return;
            }

            ServeStatic(ctx, path);
        }

        /// <summary>Строки лога операций, которые уходят в браузер, дублируются в журнал.</summary>
        private static void LogResult(object data)
        {
            switch (data)
            {
                case List<string> lines: DiagnosticLog.Lines(lines); break;
                case ConnectResult connect: DiagnosticLog.Lines(connect.Log); break;
                case CableReadResult cables: DiagnosticLog.Lines(cables.Log); break;
                case WireReadResultDto wires: DiagnosticLog.Lines(wires.Log); break;
            }
        }

        private static string ReadBody(HttpListenerRequest req)
        {
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static void WriteText(HttpListenerResponse resp, string text, string fileName)
        {
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            resp.StatusCode = 200;
            resp.ContentType = "text/tab-separated-values; charset=utf-8";
            resp.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }

        private void WriteJson(HttpListenerResponse resp, int statusCode, object data)
        {
            LogResult(data);
            var json = _json.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            resp.StatusCode = statusCode;
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }

        private void ServeStatic(HttpListenerContext ctx, string path)
        {
            if (path == "/") path = "/index.html";

            // Простая защита от выхода за пределы wwwroot через "..".
            var relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_wwwroot, relative));
            if (!fullPath.StartsWith(_wwwroot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.OutputStream.Close();
                return;
            }

            ctx.Response.ContentType = ContentTypeFor(fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static string ContentTypeFor(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".svg": return "image/svg+xml; charset=utf-8";
                case ".png": return "image/png";
                default: return "application/octet-stream";
            }
        }
    }
}
