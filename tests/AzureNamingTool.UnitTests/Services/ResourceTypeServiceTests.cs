using AzureNamingTool.Models;
using AzureNamingTool.Repositories.Interfaces;
using AzureNamingTool.Services;
using AzureNamingTool.Services.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace AzureNamingTool.UnitTests.Services;

public class ResourceTypeServiceTests
{
    private readonly Mock<IConfigurationRepository<ResourceType>> _mockRepository;
    private readonly Mock<IAdminLogService> _mockAdminLogService;
    private readonly Mock<IResourceConfigurationCoordinator> _mockCoordinator;
    private readonly Mock<IResourceDelimiterService> _mockDelimiterService;
    private readonly ResourceTypeService _service;

    public ResourceTypeServiceTests()
    {
        _mockRepository = new Mock<IConfigurationRepository<ResourceType>>();
        _mockAdminLogService = new Mock<IAdminLogService>();
        _mockCoordinator = new Mock<IResourceConfigurationCoordinator>();
        _mockDelimiterService = new Mock<IResourceDelimiterService>();
        _service = new ResourceTypeService(
            _mockRepository.Object,
            _mockAdminLogService.Object,
            _mockCoordinator.Object,
            _mockDelimiterService.Object);
    }

    [Fact]
    public async Task GetItemsAsync_ShouldReturnOrderedItems_WhenAdminIsFalse()
    {
        // Arrange
        var items = new List<ResourceType>
        {
            new ResourceType { Id = 2, Resource = "Type2", Enabled = true },
            new ResourceType { Id = 1, Resource = "Type1", Enabled = true }
        };
        _mockRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(items);

        // Act
        var result = await _service.GetItemsAsync(false);

        // Assert
        result.Success.Should().BeTrue();
        var returnedItems = result.ResponseObject as List<ResourceType>;
        returnedItems.Should().NotBeNull();
        returnedItems!.Should().HaveCount(2);
        returnedItems!.All(x => x.Enabled).Should().BeTrue();
    }

    [Fact]
    public async Task GetItemsAsync_ShouldIncludeDisabledItems_WhenAdminIsTrue()
    {
        // Arrange
        var items = new List<ResourceType>
        {
            new ResourceType { Id = 1, Resource = "Type1", Enabled = true },
            new ResourceType { Id = 2, Resource = "Type2", Enabled = false }
        };
        _mockRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(items);

        // Act
        var result = await _service.GetItemsAsync(true);

        // Assert
        result.Success.Should().BeTrue();
        var returnedItems = result.ResponseObject as List<ResourceType>;
        returnedItems.Should().NotBeNull();
        returnedItems!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetItemAsync_ShouldReturnItem_WhenItemExists()
    {
        // Arrange
        var items = new List<ResourceType>
        {
            new ResourceType { Id = 1, Resource = "Type1" }
        };
        _mockRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(items);

        // Act
        var result = await _service.GetItemAsync(1);

        // Assert
        result.Success.Should().BeTrue();
        var item = result.ResponseObject as ResourceType;
        item.Should().NotBeNull();
        item!.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetItemsAsync_ShouldLogError_WhenExceptionOccurs()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetAllAsync()).ThrowsAsync(new Exception("Database error"));
        _mockAdminLogService.Setup(s => s.PostItemAsync(It.IsAny<AdminLogMessage>())).ReturnsAsync(new ServiceResponse { Success = true });

        // Act
        var result = await _service.GetItemsAsync();

        // Assert
        result.Success.Should().BeFalse();
        _mockAdminLogService.Verify(s => s.PostItemAsync(It.Is<AdminLogMessage>(m => m.Title == "ERROR")), Times.Once);
    }

    [Fact]
    public async Task PostItemAsync_ShouldAssignCorrectId_WhenItemsHaveBeenDeleted()
    {
        // Arrange - Simulate scenario where IDs 1, 2, 3 exist and ID 2 was deleted
        var existingItems = new List<ResourceType>
        {
            new ResourceType { Id = 1, Resource = "Virtual Machine", ShortName = "vm", Enabled = true },
            new ResourceType { Id = 3, Resource = "Storage Account", ShortName = "st", Enabled = true }
        };

        var newItem = new ResourceType
        {
            Id = 0,
            Resource = "App Service",
            ShortName = "app"
        };

        List<ResourceType>? savedItems = null;
        _mockRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(existingItems);
        _mockRepository.Setup(r => r.SaveAllAsync(It.IsAny<IEnumerable<ResourceType>>()))
            .Callback<IEnumerable<ResourceType>>(items => savedItems = items.ToList())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.PostItemAsync(newItem);

        // Assert
        result.Success.Should().BeTrue();
        savedItems.Should().NotBeNull();
        var addedItem = savedItems!.FirstOrDefault(x => x.Resource == "App Service");
        addedItem.Should().NotBeNull();
        addedItem!.Id.Should().Be(4, "because ID should be Max(existing IDs) + 1, not Count + 1");
        addedItem.Id.Should().NotBe(3, "to avoid collision with existing item");
    }

    [Fact]
    public async Task PostItemAsync_ShouldAssignId1_WhenNoItemsExist()
    {
        // Arrange
        var emptyList = new List<ResourceType>();
        var newItem = new ResourceType
        {
            Id = 0,
            Resource = "FirstResource",
            ShortName = "first"
        };

        List<ResourceType>? savedItems = null;
        _mockRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(emptyList);
        _mockRepository.Setup(r => r.SaveAllAsync(It.IsAny<IEnumerable<ResourceType>>()))
            .Callback<IEnumerable<ResourceType>>(items => savedItems = items.ToList())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.PostItemAsync(newItem);

        // Assert
        result.Success.Should().BeTrue();
        savedItems.Should().NotBeNull();
        savedItems!.Should().HaveCount(1);
        savedItems![0].Id.Should().Be(1);
    }

    [Fact]
    public async Task UpdateTypeComponentsAsync_ShouldSaveAllResourceTypesOnce()
    {
        var resourceTypes = new List<ResourceType>
        {
            new() { Id = 1, Resource = "Type1", Optional = "", Exclude = "" },
            new() { Id = 2, Resource = "Type2", Optional = "ResourceOrg", Exclude = "" }
        };
        _mockCoordinator
            .Setup(coordinator => coordinator.GetComponentForTypeValidationAsync(7))
            .ReturnsAsync(new ServiceResponse
            {
                Success = true,
                ResponseObject = new ResourceComponent { Id = 7, Name = "ResourceUnitDept" }
            });
        _mockRepository.Setup(repository => repository.GetAllAsync()).ReturnsAsync(resourceTypes);
        _mockRepository
            .Setup(repository => repository.SaveAllAsync(It.IsAny<IEnumerable<ResourceType>>()))
            .Returns(Task.CompletedTask);

        var result = await _service.UpdateTypeComponentsAsync("optional-add", 7);

        result.Success.Should().BeTrue();
        resourceTypes.Select(type => type.Optional).Should().Equal("UnitDept", "ResourceOrg,UnitDept");
        _mockRepository.Verify(
            repository => repository.SaveAllAsync(It.Is<IEnumerable<ResourceType>>(items => items.Count() == 2)),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTypeComponentsAsync_ShouldPreserveExistingExcludeValues()
    {
        var resourceTypes = new List<ResourceType>
        {
            new() { Id = 1, Resource = "Type1", Optional = "", Exclude = "" },
            new() { Id = 2, Resource = "Type2", Optional = "", Exclude = "ResourceOrg" }
        };
        _mockCoordinator
            .Setup(coordinator => coordinator.GetComponentForTypeValidationAsync(7))
            .ReturnsAsync(new ServiceResponse
            {
                Success = true,
                ResponseObject = new ResourceComponent { Id = 7, Name = "ResourceUnitDept" }
            });
        _mockRepository.Setup(repository => repository.GetAllAsync()).ReturnsAsync(resourceTypes);
        _mockRepository
            .Setup(repository => repository.SaveAllAsync(It.IsAny<IEnumerable<ResourceType>>()))
            .Returns(Task.CompletedTask);

        var result = await _service.UpdateTypeComponentsAsync("exclude-add", 7);

        result.Success.Should().BeTrue();
        resourceTypes.Select(type => type.Exclude).Should().Equal("UnitDept", "ResourceOrg,UnitDept");
        _mockRepository.Verify(
            repository => repository.SaveAllAsync(It.Is<IEnumerable<ResourceType>>(items => items.Count() == 2)),
            Times.Once);
    }

    [Fact]
    public async Task UpdateTypeComponentsAsync_ShouldNotSave_WhenResourceTypesAreAlreadyCurrent()
    {
        var resourceTypes = new List<ResourceType>
        {
            new() { Id = 1, Resource = "Type1", Optional = "UnitDept", Exclude = "" },
            new() { Id = 2, Resource = "Type2", Optional = "ResourceOrg,UnitDept", Exclude = "" }
        };
        _mockCoordinator
            .Setup(coordinator => coordinator.GetComponentForTypeValidationAsync(7))
            .ReturnsAsync(new ServiceResponse
            {
                Success = true,
                ResponseObject = new ResourceComponent { Id = 7, Name = "ResourceUnitDept" }
            });
        _mockRepository.Setup(repository => repository.GetAllAsync()).ReturnsAsync(resourceTypes);

        var result = await _service.UpdateTypeComponentsAsync("optional-add", 7);

        result.Success.Should().BeTrue();
        ((string)result.ResponseObject!).Should().Be("Resource Types already up to date.");
        _mockRepository.Verify(
            repository => repository.SaveAllAsync(It.IsAny<IEnumerable<ResourceType>>()),
            Times.Never);
    }
}
