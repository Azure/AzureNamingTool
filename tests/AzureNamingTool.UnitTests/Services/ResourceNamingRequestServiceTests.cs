using AzureNamingTool.Models;
using AzureNamingTool.Services;
using AzureNamingTool.Services.Interfaces;
using FluentAssertions;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace AzureNamingTool.UnitTests.Services
{
    /// <summary>
    /// Unit tests for ResourceNamingRequestService
    /// </summary>
    public class ResourceNamingRequestServiceTests
    {
        private readonly Mock<IResourceComponentService> _mockComponentService;
        private readonly Mock<IResourceDelimiterService> _mockDelimiterService;
        private readonly Mock<IResourceTypeService> _mockTypeService;
        private readonly Mock<IResourceEnvironmentService> _mockEnvironmentService;
        private readonly Mock<IResourceLocationService> _mockLocationService;
        private readonly Mock<IResourceOrgService> _mockOrgService;
        private readonly Mock<IResourceProjAppSvcService> _mockProjAppSvcService;
        private readonly Mock<IResourceUnitDeptService> _mockUnitDeptService;
        private readonly Mock<IResourceFunctionService> _mockFunctionService;
        private readonly Mock<ICustomComponentService> _mockCustomComponentService;
        private readonly Mock<IGeneratedNamesService> _mockGeneratedNamesService;
        private readonly Mock<IAdminLogService> _mockAdminLogService;
        private readonly ResourceNamingRequestService _service;

        public ResourceNamingRequestServiceTests()
        {
            _mockComponentService = new Mock<IResourceComponentService>();
            _mockDelimiterService = new Mock<IResourceDelimiterService>();
            _mockTypeService = new Mock<IResourceTypeService>();
            _mockEnvironmentService = new Mock<IResourceEnvironmentService>();
            _mockLocationService = new Mock<IResourceLocationService>();
            _mockOrgService = new Mock<IResourceOrgService>();
            _mockProjAppSvcService = new Mock<IResourceProjAppSvcService>();
            _mockUnitDeptService = new Mock<IResourceUnitDeptService>();
            _mockFunctionService = new Mock<IResourceFunctionService>();
            _mockCustomComponentService = new Mock<ICustomComponentService>();
            _mockGeneratedNamesService = new Mock<IGeneratedNamesService>();
            _mockAdminLogService = new Mock<IAdminLogService>();

            _service = new ResourceNamingRequestService(
                _mockComponentService.Object,
                _mockDelimiterService.Object,
                _mockTypeService.Object,
                _mockEnvironmentService.Object,
                _mockLocationService.Object,
                _mockOrgService.Object,
                _mockProjAppSvcService.Object,
                _mockUnitDeptService.Object,
                _mockFunctionService.Object,
                _mockCustomComponentService.Object,
                _mockGeneratedNamesService.Object,
                _mockAdminLogService.Object);
        }

        #region RequestNameWithComponents Tests

        [Fact]
        public async Task RequestNameWithComponentsAsync_ShouldReturnStaticValue_WhenResourceTypeHasStaticValues()
        {
            // Arrange
            var request = new ResourceNameRequestWithComponents
            {
                ResourceType = new ResourceType
                {
                    Id = 1,
                    Resource = "staticResource",
                    ShortName = "static",
                    StaticValues = "predefined-static-name",
                    Enabled = true
                }
            };

            // Act
            var result = await _service.RequestNameWithComponentsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue("static values should always succeed");
            result.ResourceName.Should().Be("predefined-static-name", "static value should be returned directly");
            result.Message.Should().Contain("static value", "message should explain static value usage");
        }

        [Fact]
        public async Task RequestNameWithComponentsAsync_ShouldReturnError_WhenResourceTypeIsNull()
        {
            // Arrange
            var request = new ResourceNameRequestWithComponents
            {
                ResourceType = null!
            };

            // Act
            var result = await _service.RequestNameWithComponentsAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse("null resource type should fail");
            result.Message.Should().NotBeNullOrEmpty("error message should be provided");
        }

        #endregion

        #region RequestName Tests

        [Fact]
        public async Task RequestNameAsync_ShouldReturnError_WhenResourceTypeNotFound()
        {
            // Arrange
            var request = new ResourceNameRequest
            {
                ResourceType = "nonexistent"
            };

            var serviceResponse = new ServiceResponse
            {
                Success = true,
                ResponseObject = new List<ResourceType>() // Empty list
            };

            _mockTypeService
                .Setup(s => s.GetItemsAsync(false))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _service.RequestNameAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse("nonexistent resource type should fail");
            result.Message.Should().NotBeNullOrEmpty("error message should be provided");
        }

        [Fact]
        public async Task RequestNameAsync_ShouldHandleEmptyResourceType_Gracefully()
        {
            // Arrange
            var request = new ResourceNameRequest
            {
                ResourceType = string.Empty
            };

            var serviceResponse = new ServiceResponse
            {
                Success = true,
                ResponseObject = new List<ResourceType>()
            };

            _mockTypeService
                .Setup(s => s.GetItemsAsync(false))
                .ReturnsAsync(serviceResponse);

            // Act
            var result = await _service.RequestNameAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse("empty resource type should fail");
        }

        #endregion

        #region Component Validation Tests

        [Fact]
        public async Task RequestNameWithComponentsAsync_ShouldHandleDisabledResourceType()
        {
            // Arrange
            var request = new ResourceNameRequestWithComponents
            {
                ResourceType = new ResourceType
                {
                    Id = 1,
                    Resource = "disabledType",
                    ShortName = "dis",
                    Enabled = false
                },
                ResourceEnvironment = new ResourceEnvironment
                {
                    Id = 1,
                    Name = "Production",
                    ShortName = "prd"
                }
            };

            // Setup mock for resource components
            var components = new List<ResourceComponent>
            {
                new ResourceComponent
                {
                    Id = 1,
                    Name = "ResourceType",
                    DisplayName = "Resource Type",
                    Enabled = true,
                    SortOrder = 1
                }
            };

            var componentResponse = new ServiceResponse
            {
                Success = true,
                ResponseObject = components
            };

            _mockComponentService
                .Setup(s => s.GetItemsAsync(false))
                .ReturnsAsync(componentResponse);

            // Act
            var result = await _service.RequestNameWithComponentsAsync(request);

            // Assert
            result.Should().NotBeNull();
            // The service should still process disabled resource types
            // but may have different behavior
        }

        #endregion

        #region Resource Instance Tests

        [Fact]
        public void ReplaceResourceInstance_ShouldOnlyReplaceTrackedComponent_WhenInstanceIsNotLast()
        {
            const string originalName = "app-01-zone-01";

            var result = ResourceNamingRequestService.ReplaceResourceInstance(
                originalName,
                originalName.IndexOf("01", System.StringComparison.Ordinal),
                2,
                "02");

            result.Should().Be("app-02-zone-01");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task RequestNameAsync_ShouldHandleServiceException_Gracefully()
        {
            // Arrange
            var request = new ResourceNameRequest
            {
                ResourceType = "rg"
            };

            _mockTypeService
                .Setup(s => s.GetItemsAsync(It.IsAny<bool>()))
                .ThrowsAsync(new System.Exception("Service unavailable"));

            _mockAdminLogService
                .Setup(s => s.PostItemAsync(It.IsAny<AdminLogMessage>()))
                .ReturnsAsync(new ServiceResponse { Success = true });

            // Act
            var result = await _service.RequestNameWithComponentsAsync(It.IsAny<ResourceNameRequestWithComponents>());

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse("exception should result in failed response");
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldAcceptNullAzureValidationService()
        {
            // Arrange & Act
            var service = new ResourceNamingRequestService(
                _mockComponentService.Object,
                _mockDelimiterService.Object,
                _mockTypeService.Object,
                _mockEnvironmentService.Object,
                _mockLocationService.Object,
                _mockOrgService.Object,
                _mockProjAppSvcService.Object,
                _mockUnitDeptService.Object,
                _mockFunctionService.Object,
                _mockCustomComponentService.Object,
                _mockGeneratedNamesService.Object,
                _mockAdminLogService.Object,
                azureValidationService: null,
                conflictResolutionService: null);

            // Assert
            service.Should().NotBeNull("service should be created with null optional dependencies");
        }

        #endregion
    }
}
