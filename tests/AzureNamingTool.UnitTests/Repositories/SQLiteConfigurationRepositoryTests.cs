using AzureNamingTool.Data;
using AzureNamingTool.Models;
using AzureNamingTool.Repositories;
using AzureNamingTool.Services.Interfaces;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AzureNamingTool.UnitTests.Repositories;

/// <summary>
/// Tests for SQLiteConfigurationRepository to ensure EF Core-based storage works correctly.
/// Uses in-memory SQLite database for fast, isolated testing.
/// </summary>
public class SQLiteConfigurationRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ConfigurationDbContext _dbContext;
    private readonly Mock<ICacheService> _cacheServiceMock;
    private readonly SQLiteConfigurationRepository<ResourceType> _repository;

    public SQLiteConfigurationRepositoryTests()
    {
        // Create in-memory SQLite database
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ConfigurationDbContext(options);
        _dbContext.Database.EnsureCreated();

        _cacheServiceMock = new Mock<ICacheService>();
        _repository = new SQLiteConfigurationRepository<ResourceType>(_dbContext, _cacheServiceMock.Object);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnEmptyList_WhenDatabaseIsEmpty()
    {
        // Arrange
        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns((List<ResourceType>?)null);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull("repository should return empty list instead of null");
        result.Should().BeEmpty("no items should exist in empty database");
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnCachedData_WhenCacheHit()
    {
        // Arrange
        var cachedData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "rg", ShortName = "rg", Enabled = true },
            new() { Id = 2, Resource = "vm", ShortName = "vm", Enabled = true }
        };

        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns(cachedData);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2, "cached data contains 2 items");
        
        var resultList = result.ToList();
        resultList[0].Id.Should().Be(cachedData[0].Id);
        resultList[0].Resource.Should().Be(cachedData[0].Resource);
        resultList[1].Id.Should().Be(cachedData[1].Id);
        resultList[1].Resource.Should().Be(cachedData[1].Resource);
        
        // Verify cache was checked and database was NOT queried
        _cacheServiceMock.Verify(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ShouldLoadFromDatabase_WhenCacheMiss()
    {
        // Arrange
        var testData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "rg", ShortName = "rg", Enabled = true },
            new() { Id = 2, Resource = "vm", ShortName = "vm", Enabled = true }
        };

        await _dbContext.ResourceTypes.AddRangeAsync(testData);
        await _dbContext.SaveChangesAsync();

        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns((List<ResourceType>?)null);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2, "database contains 2 items");
        
        var resultList = result.ToList();
        resultList[0].Id.Should().Be(testData[0].Id);
        resultList[0].Resource.Should().Be(testData[0].Resource);
        resultList[1].Id.Should().Be(testData[1].Id);
        resultList[1].Resource.Should().Be(testData[1].Resource);

        // Verify cache was set
        _cacheServiceMock.Verify(x => x.SetCacheObject(It.IsAny<string>(), It.IsAny<List<ResourceType>>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnItem_WhenItemExists()
    {
        // Arrange
        var testItem = new ResourceType { Id = 42, Resource = "test-rg", ShortName = "trg", Enabled = true };
        await _dbContext.ResourceTypes.AddAsync(testItem);
        await _dbContext.SaveChangesAsync();

        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns((List<ResourceType>?)null);

        // Act
        var result = await _repository.GetByIdAsync(42);

        // Assert
        result.Should().NotBeNull("item with ID 42 exists");
        result!.Id.Should().Be(42);
        result.Resource.Should().Be("test-rg");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenItemDoesNotExist()
    {
        // Arrange
        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns((List<ResourceType>?)null);

        // Act
        var result = await _repository.GetByIdAsync(9999);

        // Assert
        result.Should().BeNull("item with non-existent ID should return null");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnFromCache_WhenCached()
    {
        // Arrange
        var cachedData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "rg", ShortName = "rg", Enabled = true },
            new() { Id = 2, Resource = "vm", ShortName = "vm", Enabled = true }
        };

        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns(cachedData);

        // Act
        var result = await _repository.GetByIdAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Resource.Should().Be("rg");
    }

    [Fact]
    public async Task SaveAsync_ShouldCreateNewItem_WhenItemDoesNotExist()
    {
        // Arrange
        var newItem = new ResourceType { Id = 100, Resource = "new-resource", ShortName = "nr", Enabled = true };

        // Act
        await _repository.SaveAsync(newItem);

        // Assert
        var saved = await _dbContext.ResourceTypes.FindAsync(100L);
        saved.Should().NotBeNull("item should be saved to database");
        saved!.Resource.Should().Be("new-resource");

        // Verify cache was invalidated
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ShouldUpdateExistingItem_WhenItemExists()
    {
        // Arrange
        var existingItem = new ResourceType { Id = 50, Resource = "old-resource", ShortName = "or", Enabled = true };
        await _dbContext.ResourceTypes.AddAsync(existingItem);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear(); // Clear tracking to simulate fresh load

        var updatedItem = new ResourceType { Id = 50, Resource = "updated-resource", ShortName = "ur", Enabled = false };

        // Act
        await _repository.SaveAsync(updatedItem);

        // Assert
        var saved = await _dbContext.ResourceTypes.FindAsync(50L);
        saved.Should().NotBeNull();
        saved!.Resource.Should().Be("updated-resource", "item should be updated");
        saved.ShortName.Should().Be("ur");
        saved.Enabled.Should().BeFalse();

        // Verify cache was invalidated
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ShouldThrowArgumentNullException_WhenEntityIsNull()
    {
        // Arrange
        ResourceType? nullEntity = null;

        // Act
        Func<Task> act = async () => await _repository.SaveAsync(nullEntity!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entity");
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveItem_WhenItemExists()
    {
        // Arrange
        var testItem = new ResourceType { Id = 200, Resource = "to-delete", ShortName = "td", Enabled = true };
        await _dbContext.ResourceTypes.AddAsync(testItem);
        await _dbContext.SaveChangesAsync();

        // Act
        await _repository.DeleteAsync(200);

        // Assert
        var deleted = await _dbContext.ResourceTypes.FindAsync(200L);
        deleted.Should().BeNull("item should be deleted from database");

        // Verify cache was invalidated
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldNotThrow_WhenItemDoesNotExist()
    {
        // Arrange & Act
        Func<Task> act = async () => await _repository.DeleteAsync(9999);

        // Assert
        await act.Should().NotThrowAsync("deleting non-existent item should be safe");
        
        // Cache should not be invalidated if nothing was deleted
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnTrue_WhenItemExists()
    {
        // Arrange
        var testItem = new ResourceType { Id = 300, Resource = "exists", ShortName = "ex", Enabled = true };
        await _dbContext.ResourceTypes.AddAsync(testItem);
        await _dbContext.SaveChangesAsync();

        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns((List<ResourceType>?)null);

        // Act
        var exists = await _repository.ExistsAsync(300);

        // Assert
        exists.Should().BeTrue("item with ID 300 exists");
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenItemDoesNotExist()
    {
        // Arrange
        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceType>>(It.IsAny<string>()))
            .Returns((List<ResourceType>?)null);

        // Act
        var exists = await _repository.ExistsAsync(9999);

        // Assert
        exists.Should().BeFalse("non-existent item should return false");
    }

    [Fact]
    public async Task SaveAllAsync_ShouldReplaceAllEntities_WhenCalled()
    {
        // Arrange
        var existingData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "old1", ShortName = "o1", Enabled = true },
            new() { Id = 2, Resource = "old2", ShortName = "o2", Enabled = true }
        };
        await _dbContext.ResourceTypes.AddRangeAsync(existingData);
        await _dbContext.SaveChangesAsync();

        var newData = new List<ResourceType>
        {
            new() { Id = 10, Resource = "new1", ShortName = "n1", Enabled = true },
            new() { Id = 20, Resource = "new2", ShortName = "n2", Enabled = true },
            new() { Id = 30, Resource = "new3", ShortName = "n3", Enabled = true }
        };

        // Act
        await _repository.SaveAllAsync(newData);

        // Assert
        var allItems = await _dbContext.ResourceTypes.ToListAsync();
        allItems.Should().HaveCount(3, "old data should be replaced with new data");
        allItems.Should().NotContain(x => x.Id == 1 || x.Id == 2, "old items should be removed");
        allItems.Should().Contain(x => x.Id == 10 && x.Resource == "new1");
        allItems.Should().Contain(x => x.Id == 20 && x.Resource == "new2");
        allItems.Should().Contain(x => x.Id == 30 && x.Resource == "new3");

        // Verify cache was invalidated
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SaveAllAsync_ShouldReplaceEntities_WhenPrimaryKeysAreReused()
    {
        var existingData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "old1", ShortName = "o1", Enabled = true },
            new() { Id = 2, Resource = "old2", ShortName = "o2", Enabled = true }
        };
        await _dbContext.ResourceTypes.AddRangeAsync(existingData);
        await _dbContext.SaveChangesAsync();

        var replacementData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "new1", ShortName = "n1", Enabled = true },
            new() { Id = 2, Resource = "new2", ShortName = "n2", Enabled = true }
        };

        await _repository.SaveAllAsync(replacementData);

        var allItems = await _dbContext.ResourceTypes.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        allItems.Select(x => x.Id).Should().Equal(1, 2);
        allItems.Select(x => x.Resource).Should().Equal("new1", "new2");
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SaveAllAsync_ShouldThrowArgumentNullException_WhenEntitiesIsNull()
    {
        // Arrange
        IEnumerable<ResourceType>? nullEntities = null;

        // Act
        Func<Task> act = async () => await _repository.SaveAllAsync(nullEntities!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entities");
    }

    [Fact]
    public async Task SaveAllAsync_ShouldRollbackOnError_WhenExceptionOccurs()
    {
        // Arrange
        var existingData = new List<ResourceType>
        {
            new() { Id = 1, Resource = "original", ShortName = "o", Enabled = true }
        };
        await _dbContext.ResourceTypes.AddRangeAsync(existingData);
        await _dbContext.SaveChangesAsync();

        // Create invalid data (duplicate IDs will cause constraint violation on second save)
        var invalidData = new List<ResourceType>
        {
            new() { Id = 100, Resource = "test1", ShortName = "t1", Enabled = true },
            new() { Id = 100, Resource = "test2", ShortName = "t2", Enabled = true } // Duplicate ID
        };

        // Act
        Func<Task> act = async () => await _repository.SaveAllAsync(invalidData);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed to save all entities");

        // Verify original data is still intact (transaction rolled back)
        var remainingItems = await _dbContext.ResourceTypes.ToListAsync();
        remainingItems.Should().Contain(x => x.Id == 1 && x.Resource == "original", 
            "original data should remain after rollback");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDbContextIsNull()
    {
        // Arrange & Act
        Action act = () => new SQLiteConfigurationRepository<ResourceType>(null!, _cacheServiceMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCacheServiceIsNull()
    {
        // Arrange & Act
        Action act = () => new SQLiteConfigurationRepository<ResourceType>(_dbContext, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cacheService");
    }

    [Fact]
    public async Task Repository_ShouldWorkWithDifferentEntityTypes()
    {
        // Arrange
        var locationRepo = new SQLiteConfigurationRepository<ResourceLocation>(_dbContext, _cacheServiceMock.Object);
        var testLocation = new ResourceLocation 
        { 
            Id = 1, 
            Name = "East US", 
            ShortName = "eus", 
            Enabled = true 
        };

        _cacheServiceMock.Setup(x => x.GetCacheObject<List<ResourceLocation>>(It.IsAny<string>()))
            .Returns((List<ResourceLocation>?)null);

        // Act
        await locationRepo.SaveAsync(testLocation);
        var retrieved = await locationRepo.GetByIdAsync(1);

        // Assert
        retrieved.Should().NotBeNull("repository should work with ResourceLocation type");
        retrieved!.Name.Should().Be("East US");
        retrieved.ShortName.Should().Be("eus");
    }

    [Fact]
    public async Task CacheInvalidation_ShouldOccur_OnAllModifyingOperations()
    {
        // Arrange
        var testItem = new ResourceType { Id = 500, Resource = "test", ShortName = "t", Enabled = true };

        // Act & Assert - Save operation
        await _repository.SaveAsync(testItem);
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once, 
            "cache should be invalidated after Save");

        _cacheServiceMock.Reset();

        // Act & Assert - Delete operation
        await _repository.DeleteAsync(500);
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once, 
            "cache should be invalidated after Delete");

        _cacheServiceMock.Reset();

        // Act & Assert - SaveAll operation
        await _repository.SaveAllAsync(new List<ResourceType> { testItem });
        _cacheServiceMock.Verify(x => x.InvalidateCacheObject(It.IsAny<string>()), Times.Once, 
            "cache should be invalidated after SaveAll");
    }
}
