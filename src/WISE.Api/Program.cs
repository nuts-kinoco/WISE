using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WISE.Domain.Interfaces;
using WISE.Domain.Services;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Services;
using WISE.Infrastructure.Providers;
using WISE.Application.Services;
using WISE.Api.UseCases;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Register UseCases and Services
builder.Services.AddScoped<ImportUseCase>();
builder.Services.AddScoped<CreateImportJobUseCase>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IMetadataProvider, JavBusMetadataProvider>();
builder.Services.AddSingleton<WISE.Application.Services.IJobCancellationService, WISE.Application.Services.JobCancellationService>();
builder.Services.AddHostedService<WISE.Api.Services.BackgroundJobWorker>();
builder.Services.AddHostedService<WISE.Api.Services.WatchFolderMonitorService>();
builder.Services.AddScoped<WISE.Api.UseCases.ExecuteImportJobUseCase>(); // Sprint 9: Synchronous execution
builder.Services.AddScoped<WISE.Api.UseCases.FetchMetadataJobUseCase>();
builder.Services.AddSingleton<WISE.Domain.Interfaces.IOutputPathResolver, WISE.Infrastructure.Services.DefaultOutputPathResolver>();

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
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WiseDbContext>();
    dbContext.Database.Migrate();
}

app.Run();
