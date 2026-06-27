using Microsoft.Extensions.DependencyInjection;
using WISE.Core.Interfaces;

namespace WISE.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWiseInfrastructure(this IServiceCollection services)
    {
        // ロガーなどのインフラストラクチャサービスを登録する
        services.AddSingleton<ILogger, ConsoleLoggerMock>(); // Mockの登録例
        
        return services;
    }
}

// 最初の起動・テスト用モックロガー
public class ConsoleLoggerMock : ILogger
{
    public void LogInformation(string message) => System.Console.WriteLine($"[INFO] {message}");
    public void LogWarning(string message) => System.Console.WriteLine($"[WARN] {message}");
    public void LogError(string message, System.Exception? ex = null) => System.Console.WriteLine($"[ERROR] {message} {ex}");
}
