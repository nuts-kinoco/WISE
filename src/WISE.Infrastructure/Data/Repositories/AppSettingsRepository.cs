using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Domain.Interfaces;

namespace WISE.Infrastructure.Data.Repositories;

public class AppSettingsRepository : IAppSettingsRepository
{
    private readonly WiseDbContext _db;

    public AppSettingsRepository(WiseDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public async Task<AppSetting?> GetAsync(string key, CancellationToken ct = default)
        => await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            _db.AppSettings.Add(new AppSetting(key, value));
        }
        else
        {
            // 追跡中のエンティティなので SetValue の変更検知だけで保存される
            existing.SetValue(value);
        }
        await _db.SaveChangesAsync(ct);
    }
}
