using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WISE.Application.UseCases;
using WISE.Domain.Interfaces;
using WISE.Domain.Entities;
using WISE.Domain.SeedWork;
using WISE.Domain.ValueObjects;
using WISE.Domain.Events;
using Xunit;

namespace WISE.Tests.Application;

public class ProcessNewAssetUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldCreateNewWork_WhenDecisionIsNew()
    {
        // Arrange
        var mockResolver = new Mock<IIdentifierResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<Asset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentifierResult(Decision.New, new ConfidenceScore(50), null, new List<Evidence>()));

        var mockRepo = new Mock<IWorkRepository>();
        var mockUow = new Mock<IUnitOfWork>();
        var mockEventBus = new Mock<IEventBus>();

        var useCase = new ProcessNewAssetUseCase(mockResolver.Object, mockRepo.Object, mockUow.Object, mockEventBus.Object);

        // Act
        var result = await useCase.ExecuteAsync("/test.mp4", "test.mp4", 1024);

        // Assert
        result.Should().NotBeNull();
        result.IsNewWork.Should().BeTrue();
        
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Work>(), It.IsAny<CancellationToken>()), Times.Once);
        mockUow.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(e => e.PublishAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
    
    [Fact]
    public async Task ExecuteAsync_ShouldUseExistingWork_WhenDecisionIsExisting()
    {
        // Arrange
        var existingWorkId = Guid.NewGuid();
        var existingWork = new Work();
        var mockResolver = new Mock<IIdentifierResolver>();
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<Asset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentifierResult(Decision.Existing, new ConfidenceScore(90), existingWorkId, new List<Evidence>()));

        var mockRepo = new Mock<IWorkRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(existingWorkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingWork);

        var mockUow = new Mock<IUnitOfWork>();
        var mockEventBus = new Mock<IEventBus>();

        var useCase = new ProcessNewAssetUseCase(mockResolver.Object, mockRepo.Object, mockUow.Object, mockEventBus.Object);

        // Act
        var result = await useCase.ExecuteAsync("/test.mp4", "test.mp4", 1024);

        // Assert
        result.Should().NotBeNull();
        result.IsNewWork.Should().BeFalse();
        result.WorkId.Should().Be(existingWork.Id);
        
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Work>(), It.IsAny<CancellationToken>()), Times.Never);
        mockUow.Verify(u => u.SaveEntitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockEventBus.Verify(e => e.PublishAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
