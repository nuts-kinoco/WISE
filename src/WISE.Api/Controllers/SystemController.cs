using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WISE.Api.UseCases;
using WISE.Application.Queries;

namespace WISE.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private readonly ISystemHistoryQueryService _historyQuery;
        private readonly SystemMaintenanceUseCase _maintenanceUseCase;

        public SystemController(ISystemHistoryQueryService historyQuery, SystemMaintenanceUseCase maintenanceUseCase)
        {
            _historyQuery = historyQuery;
            _maintenanceUseCase = maintenanceUseCase;
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int limit = 200, CancellationToken ct = default)
        {
            limit = Math.Clamp(limit, 1, 1000);
            var logs = await _historyQuery.GetHistoryAsync(limit, ct);
            return Ok(logs);
        }

        [HttpGet("history/count")]
        public async Task<IActionResult> GetHistoryCount(CancellationToken ct)
        {
            var count = await _historyQuery.GetHistoryCountAsync(ct);
            return Ok(new { count });
        }

        [HttpDelete("history")]
        public async Task<IActionResult> ClearHistory(CancellationToken ct)
        {
            var count = await _maintenanceUseCase.ClearHistoryAsync(ct);
            return Ok(new { deleted = count });
        }

        // v1.0 In-Memory Job Management
        private static readonly List<object> _mockJobs = new List<object>
        {
            new { Id = Guid.NewGuid(), Name = "Full Library Scan", Status = "Running", StartTime = DateTime.UtcNow.AddMinutes(-12), Duration = "12m 30s", Progress = 45, Error = (string?)null, TargetWorkId = (string?)null },
            new { Id = Guid.NewGuid(), Name = "Scheduled Backup", Status = "Pending", StartTime = (DateTime?)null, Duration = "-", Progress = 0, Error = (string?)null, TargetWorkId = (string?)null }
        };

        [HttpGet("jobs")]
        public IActionResult GetJobs()
        {
            // Future feature: Actually link this to a background job orchestrator.
            // For v1.0, user allowed in-memory.
            return Ok(_mockJobs);
        }

        private static readonly string WiseAppDataDir =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WISE");

        /// <summary>FC2セッションCookieをファイルに保存する。</summary>
        [HttpPost("cookies/fc2")]
        public IActionResult SaveFc2Cookies([FromBody] System.Text.Json.JsonElement body)
        {
            string? cookieValue = null;
            if (body.TryGetProperty("cookies", out var el)) cookieValue = el.GetString();
            if (cookieValue == null) return BadRequest(new { Error = "cookies field is required." });

            try
            {
                System.IO.Directory.CreateDirectory(WiseAppDataDir);
                var filePath = System.IO.Path.Combine(WiseAppDataDir, "fc2Cookies.txt");

                if (string.IsNullOrWhiteSpace(cookieValue))
                    System.IO.File.Delete(filePath);
                else
                    System.IO.File.WriteAllText(filePath, cookieValue.Trim());

                return Ok(new { saved = !string.IsNullOrWhiteSpace(cookieValue), path = filePath });
            }
            catch (Exception ex) { return StatusCode(500, new { Error = ex.Message }); }
        }

        /// <summary>FC2 Cookie ファイルが存在するか確認する。</summary>
        [HttpGet("cookies/fc2/status")]
        public IActionResult GetFc2CookieStatus()
        {
            var filePath = System.IO.Path.Combine(WiseAppDataDir, "fc2Cookies.txt");
            var storageStatePath = System.IO.Path.Combine(WiseAppDataDir, "fc2StorageState.json");
            bool hasCookieTxt = System.IO.File.Exists(filePath);
            bool hasStorageState = System.IO.File.Exists(storageStatePath);
            string? cookiePreview = null;
            if (hasCookieTxt)
            {
                var raw = System.IO.File.ReadAllText(filePath).Trim();
                cookiePreview = raw.Length > 60 ? raw[..60] + "…" : raw;
            }
            return Ok(new { hasCookieTxt, hasStorageState, cookiePreview, storageStatePath, cookieTxtPath = filePath });
        }

        /// <summary>MGStage セッションCookieをファイルに保存する。</summary>
        [HttpPost("cookies/mgs")]
        public IActionResult SaveMgsCookies([FromBody] System.Text.Json.JsonElement body)
        {
            string? cookieValue = null;
            if (body.TryGetProperty("cookies", out var el)) cookieValue = el.GetString();
            if (cookieValue == null) return BadRequest(new { Error = "cookies field is required." });

            try
            {
                System.IO.Directory.CreateDirectory(WiseAppDataDir);
                var filePath = System.IO.Path.Combine(WiseAppDataDir, "mgsCookies.txt");

                if (string.IsNullOrWhiteSpace(cookieValue))
                    System.IO.File.Delete(filePath);
                else
                    System.IO.File.WriteAllText(filePath, cookieValue.Trim());

                return Ok(new { Saved = true });
            }
            catch (Exception ex) { return StatusCode(500, new { Error = ex.Message }); }
        }

        /// <summary>MGS Cookie ファイルが存在するか確認する。</summary>
        [HttpGet("cookies/mgs/status")]
        public IActionResult GetMgsCookieStatus()
        {
            var filePath = System.IO.Path.Combine(WiseAppDataDir, "mgsCookies.txt");
            bool hasCookieTxt = System.IO.File.Exists(filePath);
            string? cookiePreview = null;
            if (hasCookieTxt)
            {
                var raw = System.IO.File.ReadAllText(filePath).Trim();
                cookiePreview = raw.Length > 60 ? raw[..60] + "…" : raw;
            }
            return Ok(new { hasCookieTxt, cookiePreview, cookieTxtPath = filePath });
        }

        /// <summary>指定パスをエクスプローラーで開く（Windows専用）。</summary>
        [HttpPost("open-path")]
        public IActionResult OpenPath([FromBody] System.Text.Json.JsonElement body)
        {
            string? path = null;
            if (body.TryGetProperty("path", out var el)) path = el.GetString();
            if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { Error = "path is required." });

            try
            {
                if (System.IO.File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (System.IO.Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                else
                    return NotFound(new { Error = "Path does not exist." });
                return Ok(new { path });
            }
            catch (Exception ex) { return StatusCode(500, new { Error = ex.Message }); }
        }

        /// <summary>
        /// Windows のフォルダ選択ダイアログを開き、選択されたパスを返す。
        /// </summary>
        [HttpGet("browse-folder")]
        public IActionResult BrowseFolder([FromQuery] string? initialPath = null)
        {
            string? selectedPath = null;
            Exception? dialogError = null;

            var staThread = new Thread(() =>
            {
                try
                {
                    // WinForms の FolderBrowserDialog は STA スレッドが必要
                    using var dialog = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = "フォルダを選択してください",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = true,
                    };

                    if (!string.IsNullOrWhiteSpace(initialPath) && System.IO.Directory.Exists(initialPath))
                        dialog.InitialDirectory = initialPath;

                    var result = dialog.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK)
                        selectedPath = dialog.SelectedPath;
                }
                catch (Exception ex)
                {
                    dialogError = ex;
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join(30_000); // 30秒タイムアウト

            if (dialogError != null)
                return StatusCode(500, dialogError.Message);

            if (selectedPath == null)
                return Ok(new { cancelled = true, path = (string?)null });

            return Ok(new { cancelled = false, path = selectedPath });
        }
    }
}
