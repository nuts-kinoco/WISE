using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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

// Metadata providers — ConflictResolver が全Providerの結果をマージし、最高信頼度を primary とする
// Tier0: ローカル（Priority=100, 即時・信頼性最高）
builder.Services.AddScoped<IMetadataProvider, ComicInfoXmlMetadataProvider>(); // Priority=100（ComicInfo.xml内蔵）
// Tier1: 公式・販売元（Priority≥70）
builder.Services.AddScoped<IMetadataProvider, DLSiteMetadataProvider>();  // Priority=80（同人RJ/VJ/BJ）
builder.Services.AddScoped<IMetadataProvider, FanzaMetadataProvider>();   // Priority=80
builder.Services.AddScoped<IMetadataProvider, GetchuMetadataProvider>();  // Priority=70（同人/ゲーム）
builder.Services.AddScoped<IMetadataProvider, MgsMetadataProvider>();     // Priority=70（Cookie要）
builder.Services.AddScoped<IMetadataProvider, Fc2MetadataProvider>();     // Priority=60（FC2識別子専用）
// Tier2: フォールバック補完（Priority<70）
builder.Services.AddScoped<IMetadataProvider, Fc2AltMetadataProvider>(); // Priority=55（FC2削除済みコンテンツ）
builder.Services.AddScoped<IMetadataProvider, AvWikiMetadataProvider>(); // Priority=60（日本語DB・補完用）
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
    options.UseSqlite($"Data Source={dbPath}");
});
builder.Services.AddScoped<IWorkRepository, WISE.Infrastructure.Data.Repositories.WorkRepository>();
builder.Services.AddScoped<IReadingHistoryRepository, WISE.Infrastructure.Data.Repositories.ReadingHistoryRepository>();
builder.Services.AddScoped<ICoverCacheRepository, WISE.Infrastructure.Data.Repositories.CoverCacheRepository>();
builder.Services.AddScoped<IDisplayProfileRepository, WISE.Infrastructure.Data.Repositories.DisplayProfileRepository>();
builder.Services.AddScoped<WISE.Domain.SeedWork.IUnitOfWork>(sp => sp.GetRequiredService<WiseDbContext>());

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
        USING fts5(value, content=METADATA_FIELD, content_rowid=id,
                   tokenize='unicode61 remove_diacritics 1');");
    await DisplayProfileSeeder.SeedAsync(dbContext);
}

app.Run();
