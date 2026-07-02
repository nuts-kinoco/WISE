using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using WISE.Domain.Interfaces;
using WISE.Domain.Providers;
using WISE.Domain.Services;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Services;
using WISE.Infrastructure.Providers;
using WISE.Application.Services;
using WISE.Api.UseCases;
using WISE.Infrastructure.Cookies;
using WISE.Infrastructure.Cover;
using WISE.Infrastructure.Viewers;
using WISE.Infrastructure.Archive;
using WISE.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache(options =>
{
    // 512MB 上限。各エントリが Size=bytes を報告することで上限が機能する。
    options.SizeLimit = 512L * 1024 * 1024;
});
// Video stream cache
builder.Services.Configure<WISE.Infrastructure.Services.VideoCacheOptions>(
    builder.Configuration.GetSection("VideoCache"));
builder.Services.AddSingleton<WISE.Infrastructure.Services.VideoStreamCache>();

// Register UseCases and Services
builder.Services.AddScoped<ImportUseCase>();
builder.Services.AddScoped<CreateImportJobUseCase>();
builder.Services.AddHttpClient();

// Cookie Policies
builder.Services.AddScoped<ICookiePolicy, WISE.Infrastructure.Cookies.FanzaCookiePolicy>();
builder.Services.AddScoped<ICookiePolicy, WISE.Infrastructure.Cookies.MgsCookiePolicy>();
builder.Services.AddScoped<ICookiePolicy, WISE.Infrastructure.Cookies.Fc2CookiePolicy>();
builder.Services.AddScoped<ICookieProvider, WISE.Infrastructure.Cookies.CookieProvider>();

// Named Options バインディング（appsettings.json の MetadataProviders セクション）
builder.Services.Configure<WISE.Domain.Models.MetadataProviderOptions>("Fanza",
    builder.Configuration.GetSection("MetadataProviders:Fanza"));
builder.Services.Configure<WISE.Domain.Models.MetadataProviderOptions>("Mgs",
    builder.Configuration.GetSection("MetadataProviders:Mgs"));
builder.Services.Configure<WISE.Domain.Models.MetadataProviderOptions>("Fc2",
    builder.Configuration.GetSection("MetadataProviders:Fc2"));
builder.Services.Configure<WISE.Domain.Models.MetadataProviderOptions>("LocalNfo",
    builder.Configuration.GetSection("MetadataProviders:LocalNfo"));

// Rate limiter (singleton — holds per-domain bucket state)
builder.Services.AddSingleton<RateLimiterService>();
// DelegatingHandlers — transient は HttpClient pipeline の規約
builder.Services.AddTransient<RateLimitingHandler>();
builder.Services.AddTransient<CachingHandler>();

// Metadata providers — ConflictResolver が全Providerの結果をマージし、最高信頼度を primary とする
// HTTP を使うプロバイダーは typed client として登録し Polly retry を適用する（3回、指数バックオフ）
static IAsyncPolicy<System.Net.Http.HttpResponseMessage> MetadataRetryPolicy() =>
    Policy<System.Net.Http.HttpResponseMessage>
        .Handle<System.Net.Http.HttpRequestException>()
        .OrResult(r => (int)r.StatusCode >= 500)
        .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)));

// Tier0: ローカル（Priority=100, 即時・信頼性最高）—— HTTP なし、通常登録
builder.Services.AddScoped<IMetadataProvider, ComicInfoXmlMetadataProvider>(); // Priority=100
builder.Services.AddScoped<IMetadataProvider, DoujinishiFilenameMetadataProvider>(); // Priority=50
// Tier1: 公式・販売元 — CachingHandler (外) → RateLimitingHandler → Polly retry (内)
// Cache hit なら rate limit も network も発生しない
builder.Services.AddHttpClient<DLSiteMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<DLSiteMetadataProvider>());
builder.Services.AddHttpClient<FanzaMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<FanzaMetadataProvider>());
builder.Services.AddHttpClient<GetchuMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<GetchuMetadataProvider>());
builder.Services.AddHttpClient<MgsMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<MgsMetadataProvider>());
builder.Services.AddHttpClient<Fc2MetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<Fc2MetadataProvider>());
// Tier2: フォールバック補完
builder.Services.AddHttpClient<Fc2AltMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<Fc2AltMetadataProvider>());
builder.Services.AddHttpClient<AvWikiMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<AvWikiMetadataProvider>());
builder.Services.AddHttpClient<JavBusMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<JavBusMetadataProvider>());
// JavLibrary: HttpClient ではなく Playwright 経由（Cloudflare 対策）
builder.Services.AddSingleton<PlaywrightBrowserService>();
builder.Services.AddScoped<JavLibraryMetadataProvider>();
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<JavLibraryMetadataProvider>());
// AdultWiki: Tier-2 最終フォールバック（RPIN 系など公式未収録品番のカバレッジ補完、Priority=30）
builder.Services.AddHttpClient<AdultWikiMetadataProvider>()
    .AddHttpMessageHandler<CachingHandler>()
    .AddHttpMessageHandler<RateLimitingHandler>()
    .AddPolicyHandler(MetadataRetryPolicy());
builder.Services.AddScoped<IMetadataProvider>(sp => sp.GetRequiredService<AdultWikiMetadataProvider>());
builder.Services.AddScoped<MetadataService>();
builder.Services.AddScoped<IMetadataConflictResolver, MetadataConflictResolver>();
builder.Services.AddSingleton<WISE.Application.Services.IJobCancellationService, WISE.Application.Services.JobCancellationService>();
builder.Services.AddHostedService<WISE.Api.Services.BackgroundJobWorker>();
builder.Services.AddHostedService<WISE.Api.Services.WatchFolderMonitorService>();
builder.Services.AddScoped<WISE.Api.UseCases.ExecuteImportJobUseCase>();
builder.Services.AddScoped<WISE.Api.UseCases.FetchMetadataJobUseCase>();
builder.Services.AddSingleton<WISE.Domain.Interfaces.IOutputPathResolver, WISE.Infrastructure.Services.DefaultOutputPathResolver>();
builder.Services.AddScoped<WISE.Infrastructure.Services.FFmpegThumbnailService>();

// Sprint 13: Evidence-Based Identifier Resolution Pipeline
// IEvidenceProvider は複数登録可能。優先度は登録順ではなく Score の合計で決まる。
builder.Services.AddScoped<IEvidenceProvider, FileNameEvidenceProvider>();
builder.Services.AddScoped<IEvidenceProvider, WISE.Domain.Providers.PathEvidenceProvider>();
builder.Services.AddScoped<IEvidenceProvider, WISE.Infrastructure.Providers.ComicInfoXmlEvidenceProvider>();
builder.Services.AddScoped<IIdentifierResolver, IdentifierResolver>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:3000") // Next.js default port
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// Configure Database
var appDataPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "WISE");
Directory.CreateDirectory(appDataPath);
var dbPath = Path.Combine(appDataPath, "wise.db");

builder.Services.AddDbContext<WiseDbContext>(options =>
{
    // Foreign Keys=True: 接続文字列で FK を有効化（接続プール再利用時も確実に有効）
    // SqlitePragmaInterceptor も併用し、両方で担保する。
    options.UseSqlite($"Data Source={dbPath};Foreign Keys=True");
    options.AddInterceptors(new WISE.Infrastructure.Data.SqlitePragmaInterceptor());
});
builder.Services.AddScoped<IWorkRepository, WISE.Infrastructure.Data.Repositories.WorkRepository>();
builder.Services.AddScoped<IReadingHistoryRepository, WISE.Infrastructure.Data.Repositories.ReadingHistoryRepository>();
builder.Services.AddScoped<ICoverCacheRepository, WISE.Infrastructure.Data.Repositories.CoverCacheRepository>();
builder.Services.AddScoped<IDisplayProfileRepository, WISE.Infrastructure.Data.Repositories.DisplayProfileRepository>();
builder.Services.AddScoped<WISE.Domain.SeedWork.IUnitOfWork>(sp => sp.GetRequiredService<WiseDbContext>());

// P1リファクタリング（DbContext直接注入の是正）: Query Service / UseCase / Repository
builder.Services.AddScoped<WISE.Application.Queries.IHistoryQueryService, WISE.Infrastructure.Data.Queries.HistoryQueryService>();
builder.Services.AddScoped<WISE.Application.Queries.IHomeQueryService, WISE.Infrastructure.Data.Queries.HomeQueryService>();
builder.Services.AddScoped<WISE.Api.UseCases.WatchFolderUseCase>();
builder.Services.AddScoped<IAppSettingsRepository, WISE.Infrastructure.Data.Repositories.AppSettingsRepository>();
builder.Services.AddScoped<WISE.Application.Queries.ICollectionsQueryService, WISE.Infrastructure.Data.Queries.CollectionsQueryService>();
builder.Services.AddScoped<WISE.Api.UseCases.CollectionUseCase>();
builder.Services.AddScoped<WISE.Application.Queries.IReaderQueryService, WISE.Infrastructure.Data.Queries.ReaderQueryService>();
builder.Services.AddScoped<WISE.Application.Queries.IAssetsQueryService, WISE.Infrastructure.Data.Queries.AssetsQueryService>();

// Archive readers
builder.Services.AddScoped<IArchiveReader, ZipArchiveReader>();
builder.Services.AddScoped<IArchiveReader, RarArchiveReader>();
builder.Services.AddScoped<IArchiveReader, FolderArchiveReader>();
builder.Services.AddScoped<IArchiveReader, PdfArchiveReader>();
builder.Services.AddScoped<IArchiveReader, EpubArchiveReader>();
builder.Services.AddScoped<ArchiveReaderSelector>();

// Cover providers (Chain of Responsibility)
builder.Services.AddScoped<ICoverProviderChain, CoverProviderChain>();
builder.Services.AddScoped<ICoverProvider, AssetCoverProvider>();
builder.Services.AddScoped<ICoverProvider, EpubCoverProvider>();
builder.Services.AddScoped<ICoverProvider, ArchiveCoverProvider>();
builder.Services.AddScoped<ICoverProvider, SampleImageCoverProvider>();
builder.Services.AddScoped<ICoverProvider, VideoThumbnailCoverProvider>();
builder.Services.AddScoped<ICoverProvider, DefaultCoverProvider>();
builder.Services.AddScoped<FFmpegThumbnailService>();

// Media viewers (Strategy per MediaType — no switch/if on MediaType in Application/Domain)
builder.Services.AddScoped<IMediaViewer, VideoMediaViewer>();
builder.Services.AddScoped<IMediaViewer, ComicMediaViewer>();
builder.Services.AddScoped<IMediaViewer, BookMediaViewer>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

// Apply migrations and seed data on startup
using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WiseDbContext>();
    dbContext.Database.Migrate();
    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    dbContext.Database.ExecuteSqlRaw(@"
        CREATE VIRTUAL TABLE IF NOT EXISTS METADATA_FIELD_FTS
        USING fts5(value, content=MetadataFields, content_rowid=Id,
                   tokenize='unicode61 remove_diacritics 1');");
    // 前回の起動で中断されたジョブをリセット
    dbContext.Database.ExecuteSqlRaw(@"
        UPDATE Jobs
        SET Status = 4, FinishedAt = datetime('now'), ErrorMessage = 'Interrupted (server restart)'
        WHERE Status IN (1, 2);");
    await DisplayProfileSeeder.SeedAsync(dbContext);
}

app.Run();
