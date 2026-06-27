using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WISE.Domain.Entities;
using WISE.Infrastructure.Data;
using WISE.Infrastructure.Data.Repositories;
using WISE.Domain.Interfaces;
using Xunit;

namespace WISE.Tests.Infrastructure.Data;

public class WorkRepositoryTests
{
    private DbContextOptions<WiseDbContext> CreateNewContextOptions()
    {
        return new DbContextOptionsBuilder<WiseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task AddAsync_ShouldAddWorkToDatabase()
    {
        // Arrange
        var options = CreateNewContextOptions();
        var work = new Work("TEST-001");
        var asset = new Asset("/test.mp4", "test.mp4", 1024);
        work.AddAsset(asset);

        using (var context = new WiseDbContext(options))
        {
            var repository = new WorkRepository(context);
            
            // Act
            await repository.AddAsync(work);
            await context.SaveChangesAsync();
        }

        // Assert
        using (var context = new WiseDbContext(options))
        {
            var repository = new WorkRepository(context);
            var savedWork = await repository.GetByIdAsync(work.Id);
            
            savedWork.Should().NotBeNull();
            savedWork!.PrimaryIdentifier.Should().Be("TEST-001");
            savedWork.Assets.Should().HaveCount(1);
            savedWork.Assets.First().FilePath.Should().Be("/test.mp4");
        }
    }
}
