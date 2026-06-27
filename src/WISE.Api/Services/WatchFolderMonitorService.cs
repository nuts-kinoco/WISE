using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WISE.Application.DTOs;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;

namespace WISE.Api.Services;

public class WatchFolderMonitorService : BackgroundService
{
    private readonly ILogger<WatchFolderMonitorService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _pendingFiles = new();

    public WatchFolderMonitorService(ILogger<WatchFolderMonitorService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WatchFolderMonitorService starting.");

        // Periodically refresh watch folders from DB
        var refreshTask = RefreshWatchersAsync(stoppingToken);
        var processTask = ProcessPendingFilesAsync(stoppingToken);

        await Task.WhenAll(refreshTask, processTask);
    }

    private async Task RefreshWatchersAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WiseDbContext>();
                
                var activeWatchFolders = await dbContext.WatchFolders
                    .Where(w => w.IsEnabled)
                    .ToListAsync(stoppingToken);

                var activePaths = activeWatchFolders.Select(w => w.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Add new watchers
                foreach (var folder in activeWatchFolders)
                {
                    if (!_watchers.ContainsKey(folder.Path) && Directory.Exists(folder.Path))
                    {
                        var watcher = new FileSystemWatcher(folder.Path)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                        };

                        watcher.Created += OnFileCreatedOrChanged;
                        watcher.Changed += OnFileCreatedOrChanged;
                        watcher.Renamed += OnFileCreatedOrChanged;
                        watcher.EnableRaisingEvents = true;

                        _watchers[folder.Path] = watcher;
                        _logger.LogInformation($"Started watching folder: {folder.Path}");
                    }
                }

                // Remove old watchers
                var keysToRemove = _watchers.Keys.Where(k => !activePaths.Contains(k)).ToList();
                foreach (var key in keysToRemove)
                {
                    if (_watchers.TryGetValue(key, out var watcher))
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                        _watchers.Remove(key);
                        _logger.LogInformation($"Stopped watching folder: {key}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing watch folders.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private void OnFileCreatedOrChanged(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.FullPath).ToLower();
        var validExtensions = new[] { ".mp4", ".mkv", ".avi", ".zip", ".jpg", ".png" };
        if (validExtensions.Contains(ext))
        {
            _pendingFiles[e.FullPath] = DateTime.UtcNow;
        }
    }

    private async Task ProcessPendingFilesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var filesToProcess = new List<string>();
                var now = DateTime.UtcNow;

                foreach (var kvp in _pendingFiles)
                {
                    var filePath = kvp.Key;
                    var lastDetected = kvp.Value;

                    // Wait at least 3 seconds since last detection
                    if ((now - lastDetected).TotalSeconds >= 3)
                    {
                        if (IsFileStabilized(filePath))
                        {
                            filesToProcess.Add(filePath);
                        }
                        else
                        {
                            // File is still being written to, update the detection time
                            _pendingFiles[filePath] = now;
                        }
                    }
                }

                if (filesToProcess.Any())
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<WiseDbContext>();

                    var request = new ImportJobRequest
                    {
                        InputFiles = filesToProcess,
                        UseMetadataPipeline = true
                    };

                    var payload = JsonSerializer.Serialize(request);
                    var job = new Job("Import", "WatchFolder", payload);
                    dbContext.Jobs.Add(job);
                    await dbContext.SaveChangesAsync(stoppingToken);

                    foreach (var file in filesToProcess)
                    {
                        _pendingFiles.TryRemove(file, out _);
                        _logger.LogInformation($"Queued Import job for stabilized file: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending files.");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private bool IsFileStabilized(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            // Try to open the file with exclusive access (read-only is fine to check if it's locked by writing process)
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            // File is locked by another process (e.g. still copying)
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Permission issue, might not be stabilized or readable yet
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
        base.Dispose();
    }
}
