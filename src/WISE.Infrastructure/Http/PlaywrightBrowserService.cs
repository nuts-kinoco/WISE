using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WISE.Infrastructure.Http;

// Singleton: Chromium ブラウザプロセスを 1 つ保持し、ページ単位でリクエストを処理する。
// Cloudflare の JS チャレンジを実際のブラウザ実行環境で突破するために使用。
//
// 事前準備（初回のみ）:
//   dotnet tool install -g Microsoft.Playwright.CLI
//   playwright install chromium
public sealed class PlaywrightBrowserService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _pageSlot = new(3, 3); // 最大 3 並行ページ
    private readonly ILogger<PlaywrightBrowserService> _logger;

    public PlaywrightBrowserService(ILogger<PlaywrightBrowserService> logger)
    {
        _logger = logger;
    }

    // url: 取得対象 URL
    // waitForSelector: ページが有効なコンテンツを持っていることを確認するセレクタ（CF チャレンジ通過後に現れる要素）
    // 戻り値: HTML 文字列。Cloudflare に遮られた場合や selector が見つからない場合は null。
    public async Task<string?> FetchHtmlAsync(string url, string waitForSelector, CancellationToken ct)
    {
        IBrowser browser;
        try { browser = await GetBrowserAsync(ct); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Playwright] ブラウザ起動に失敗。playwright install chromium が実行されているか確認してください。");
            return null;
        }

        await _pageSlot.WaitAsync(ct);
        IPage? page = null;
        try
        {
            page = await browser.NewPageAsync(new BrowserNewPageOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            });

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000,
            });

            // Cloudflare チャレンジの JS 解決 + コンテンツ描画を待つ
            try
            {
                await page.WaitForSelectorAsync(waitForSelector,
                    new PageWaitForSelectorOptions { Timeout = 12_000 });
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("[Playwright] セレクタ '{Sel}' がタイムアウト — Cloudflare が有効か結果なし: {Url}",
                    waitForSelector, url);
                return null;
            }

            var html = await page.ContentAsync();
            _logger.LogDebug("[Playwright] 取得成功 {Url} ({Bytes} bytes)", url, html.Length);
            return html;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Playwright] 取得失敗 {Url}", url);
            return null;
        }
        finally
        {
            if (page != null)
            {
                try { await page.CloseAsync(); } catch { /* ignore */ }
            }
            _pageSlot.Release();
        }
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser?.IsConnected == true) return _browser;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_browser?.IsConnected == true) return _browser;

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
            });

            _logger.LogInformation("[Playwright] Chromium 起動完了");
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            try { await _browser.DisposeAsync(); } catch { /* ignore */ }
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
    }
}
