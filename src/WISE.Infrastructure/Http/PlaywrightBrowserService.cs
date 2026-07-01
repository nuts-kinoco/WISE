using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace WISE.Infrastructure.Http;

// Singleton: Chromium ブラウザプロセスを 1 つ保持し、ステルス BrowserContext でリクエストを処理する。
// headless Chromium の自動化フィンガープリント（navigator.webdriver 等）を JS init script で除去し、
// Cloudflare Turnstile / JS チャレンジを突破する。
//
// 事前準備（初回のみ）:
//   playwright install chromium
public sealed class PlaywrightBrowserService : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;  // stealth 設定を持つ永続コンテキスト
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _pageSlot = new(3, 3);
    private readonly ILogger<PlaywrightBrowserService> _logger;

    // navigator.webdriver / plugins / chrome ランタイムを偽装して自動化検出を回避する
    private const string StealthScript = """
        Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
        Object.defineProperty(navigator, 'plugins',   { get: () => [1, 2, 3, 4, 5] });
        Object.defineProperty(navigator, 'languages', { get: () => ['ja-JP', 'ja', 'en-US', 'en'] });
        window.chrome = { runtime: {} };
        const orig = window.navigator.permissions.query;
        window.navigator.permissions.query = (p) =>
            p.name === 'notifications'
                ? Promise.resolve({ state: Notification.permission })
                : orig(p);
        """;

    public PlaywrightBrowserService(ILogger<PlaywrightBrowserService> logger)
    {
        _logger = logger;
    }

    // url: 取得対象 URL
    // waitForSelector: Cloudflare チャレンジ通過後にページ上に現れる要素のセレクタ
    // 戻り値: HTML 文字列。突破失敗・タイムアウト時は null。
    public async Task<string?> FetchHtmlAsync(string url, string waitForSelector, CancellationToken ct)
    {
        IBrowserContext context;
        try { context = await GetContextAsync(ct); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Playwright] ブラウザ/コンテキスト起動に失敗。playwright install chromium が実行されているか確認してください。");
            return null;
        }

        await _pageSlot.WaitAsync(ct);
        IPage? page = null;
        try
        {
            page = await context.NewPageAsync();

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30_000,
            });

            // Cloudflare Turnstile の JS 解決を待つ（最大30秒）
            try
            {
                await page.WaitForSelectorAsync(waitForSelector,
                    new PageWaitForSelectorOptions { Timeout = 30_000 });
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("[Playwright] セレクタ '{Sel}' がタイムアウト(30s) — Cloudflare未突破か結果なし: {Url}",
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

    private async Task<IBrowserContext> GetContextAsync(CancellationToken ct)
    {
        if (_context != null && _browser?.IsConnected == true) return _context;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_context != null && _browser?.IsConnected == true) return _context;

            // ブラウザが切断された場合は再作成
            if (_browser?.IsConnected != true)
            {
                _playwright ??= await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Args =
                    [
                        "--no-sandbox",
                        "--disable-blink-features=AutomationControlled",  // 自動化フラグを除去
                        "--disable-infobars",
                        "--window-size=1280,800",
                        "--lang=ja-JP,ja",
                    ],
                });
                _logger.LogInformation("[Playwright] Chromium 起動完了");
            }

            // ステルス設定を持つ BrowserContext を作成
            // init script はこのコンテキストから作成される全ページに自動適用される
            _context = await _browser!.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                Locale = "ja-JP",
                TimezoneId = "Asia/Tokyo",
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    { "Accept-Language", "ja,en-US;q=0.9,en;q=0.8" },
                },
            });

            await _context.AddInitScriptAsync(StealthScript);
            _logger.LogInformation("[Playwright] ステルスコンテキスト作成完了");
            return _context;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context != null)
        {
            try { await _context.DisposeAsync(); } catch { /* ignore */ }
            _context = null;
        }
        if (_browser != null)
        {
            try { await _browser.DisposeAsync(); } catch { /* ignore */ }
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
    }
}
