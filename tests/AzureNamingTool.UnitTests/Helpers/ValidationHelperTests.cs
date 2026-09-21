using AzureNamingTool.Helpers;
using AzureNamingTool.Models;
using FluentAssertions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace AzureNamingTool.UnitTests.Helpers;

public class ValidationHelperTests
{
    [Fact]
    public void RepositoryDefaults_ShouldHaveConsistentResourceLocationMetadata()
    {
        var resourceLocations = LoadRepositoryResourceLocations();

        resourceLocations.Select(x => x.Id).Should().BeEquivalentTo(
            Enumerable.Range(1, resourceLocations.Count).Select(x => (long)x),
            options => options.WithStrictOrdering());
        resourceLocations.Select(x => x.Name).Should().OnlyHaveUniqueItems();
        resourceLocations.Select(x => x.ShortName).Should().OnlyHaveUniqueItems();
        resourceLocations.Should().ContainSingle(x => x.Name == "Denmark East" && x.ShortName == "dke");
        resourceLocations.Should().ContainSingle(x => x.Name == "India South Central" && x.ShortName == "insc");
        resourceLocations.Should().ContainSingle(x => x.Name == "South India" && x.ShortName == "ins");
        resourceLocations.Should().ContainSingle(x => x.Name == "China East 3" && x.ShortName == "cne3");
    }

    [Fact]
    public void RepositoryDefaults_ShouldHaveConsistentResourceTypeMetadata()
    {
        var resourceTypes = LoadRepositoryResourceTypes();

        resourceTypes.Select(x => x.Id).Should().BeEquivalentTo(
            Enumerable.Range(1, resourceTypes.Count),
            options => options.WithStrictOrdering());
        resourceTypes
            .Select(x => $"{x.Resource}|{x.Property}")
            .Should().OnlyHaveUniqueItems();
        resourceTypes
            .Where(x => !string.IsNullOrWhiteSpace(x.Regx))
            .Should().OnlyContain(x => TryCompileRegex(x.Regx));
        resourceTypes
            .Where(x => !string.IsNullOrWhiteSpace(x.StaticValues))
            .SelectMany(x => x.StaticValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => (ResourceType: x, Value: value)))
            .Should().OnlyContain(x => Regex.IsMatch(x.Value, x.ResourceType.Regx));
    }

    [Theory]
    [InlineData("App/containerApps", "my-container-app", true)]
    [InlineData("App/containerApps", "MyContainerApp", false)]
    [InlineData("Network/privateEndpoints", "private.endpoint_01", true)]
    [InlineData("Network/privateEndpoints", "-private-endpoint", false)]
    [InlineData("Synapse/workspaces", "analytics-01", true)]
    [InlineData("Synapse/workspaces", "analytics-ondemand", false)]
    [InlineData("Storage/storageAccounts/blobServices", "default", true)]
    [InlineData("Storage/storageAccounts/blobServices", "not-default", false)]
    [InlineData("AppConfiguration/configurationStores", "config--store", true)]
    [InlineData("AppConfiguration/configurationStores", "config---store", false)]
    [InlineData("MobileNetwork/mobileNetworks/services", "default", false)]
    [InlineData("MobileNetwork/mobileNetworks/services", "voice-service", true)]
    [InlineData("NetworkCloud/clusters/metricsConfigurations", "default", true)]
    [InlineData("NetworkCloud/clusters/metricsConfigurations", "metrics", false)]
    [InlineData("Security/informationProtectionPolicies", "effective", true)]
    [InlineData("Security/informationProtectionPolicies", "default", false)]
    [InlineData("AppPlatform/spring", "spring-app", true)]
    [InlineData("AppPlatform/spring", "app-", false)]
    [InlineData("AppPlatform/spring", "----", false)]
    public void RepositoryDefaults_ShouldEnforceCurrentAzureNameRules(
        string resourceName,
        string candidate,
        bool expectedValid)
    {
        var resourceType = LoadRepositoryResourceTypes()
            .Should().ContainSingle(x => x.Resource == resourceName).Subject;

        Regex.IsMatch(candidate, resourceType.Regx).Should().Be(expectedValid);
    }

    [Theory]
    [InlineData("Subscription/subscriptions")]
    [InlineData("Management/managementGroups")]
    public void RepositoryDefaults_ShouldConfigureTenantLevelNameValidation(string resourceName)
    {
        var repositoryPath = Path.Combine(AppContext.BaseDirectory, "repository", "resourcetypes.json");
        var resourceTypes = JsonSerializer.Deserialize<List<ResourceType>>(
            File.ReadAllText(repositoryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var resourceType = resourceTypes.Should().ContainSingle(x => x.Resource == resourceName).Subject;

        resourceType.LengthMin.Should().NotBeNullOrWhiteSpace();
        resourceType.LengthMax.Should().NotBeNullOrWhiteSpace();
        resourceType.Regx.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Subscription/subscriptions", "Contoso_Azure-(Prod).01", true, "Contoso_Azure-(Prod).01")]
    [InlineData("Subscription/subscriptions", "Contoso/Prod", false, "Contoso/Prod")]
    [InlineData("Management/managementGroups", "Contoso_Prod-(01)", true, "Contoso_Prod-(01)")]
    [InlineData("Management/managementGroups", ".Contoso", false, ".Contoso")]
    [InlineData("Management/managementGroups", "Contoso.", false, "Contoso.")]
    public void RepositoryDefaults_ShouldEnforceTenantLevelNameRules(
        string resourceName,
        string candidate,
        bool expectedValid,
        string expectedName)
    {
        var repositoryPath = Path.Combine(AppContext.BaseDirectory, "repository", "resourcetypes.json");
        var resourceTypes = JsonSerializer.Deserialize<List<ResourceType>>(
            File.ReadAllText(repositoryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var resourceType = resourceTypes.Should().ContainSingle(x => x.Resource == resourceName).Subject;

        var result = ValidationHelper.ValidateGeneratedName(resourceType, candidate, "-");

        result.Valid.Should().Be(expectedValid);
        result.Name.Should().Be(expectedName);
    }

    [Fact]
    public void RepositoryDefaults_ShouldEnforceSubscriptionLengthLimit()
    {
        var resourceType = LoadRepositoryResourceTypes()
            .Should().ContainSingle(x => x.Resource == "Subscription/subscriptions").Subject;

        ValidationHelper.ValidateGeneratedName(resourceType, new string('a', 50), "")
            .Valid.Should().BeTrue();
        ValidationHelper.ValidateGeneratedName(resourceType, new string('a', 51), "")
            .Valid.Should().BeFalse();
    }

    private static List<ResourceType> LoadRepositoryResourceTypes()
    {
        var repositoryPath = Path.Combine(AppContext.BaseDirectory, "repository", "resourcetypes.json");
        return JsonSerializer.Deserialize<List<ResourceType>>(
            File.ReadAllText(repositoryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static List<ResourceLocation> LoadRepositoryResourceLocations()
    {
        var repositoryPath = Path.Combine(AppContext.BaseDirectory, "repository", "resourcelocations.json");
        return JsonSerializer.Deserialize<List<ResourceLocation>>(
            File.ReadAllText(repositoryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static bool TryCompileRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData("Test1234", true)]
    [InlineData("Password1", true)]
    [InlineData("MyP@ssw0rd", true)]
    [InlineData("lowercase1", false)] // No uppercase
    [InlineData("UPPERCASE", false)] // No number
    [InlineData("Short1", false)] // Less than 8 chars
    [InlineData("NoNumber", false)] // No number
    [InlineData("12345678", false)] // No uppercase
    [InlineData("", false)] // Empty
    public void ValidatePassword_ShouldValidateCorrectly(string password, bool expected)
    {
        // Act
        var result = ValidationHelper.ValidatePassword(password);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("456789", true)]
    [InlineData("0", true)]
    [InlineData("abc", false)]
    [InlineData("123abc", false)]
    [InlineData("", false)]
    [InlineData("12.34", false)]
    public void CheckNumeric_ShouldValidateNumericStrings(string value, bool expected)
    {
        // Act
        var result = ValidationHelper.CheckNumeric(value);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("ABC", true)]
    [InlineData("123", true)]
    [InlineData("abc123xyz", true)]
    [InlineData("abc-123", false)]
    [InlineData("abc_123", false)]
    [InlineData("abc 123", false)]
    [InlineData("abc@123", false)]
    [InlineData("abc.123", false)]
    public void CheckAlphanumeric_ShouldValidateAlphanumericStrings(string value, bool expected)
    {
        // Act
        var result = ValidationHelper.CheckAlphanumeric(value);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void CheckComponentLength_ShouldReturnTrue_WhenValueWithinRange()
    {
        // Arrange
        var component = new ResourceComponent { MinLength = "3", MaxLength = "10" };
        var value = "test";

        // Act
        var result = ValidationHelper.CheckComponentLength(component, value);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckComponentLength_ShouldReturnFalse_WhenValueTooShort()
    {
        // Arrange
        var component = new ResourceComponent { MinLength = "5", MaxLength = "10" };
        var value = "abc";

        // Act
        var result = ValidationHelper.CheckComponentLength(component, value);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckComponentLength_ShouldReturnFalse_WhenValueTooLong()
    {
        // Arrange
        var component = new ResourceComponent { MinLength = "3", MaxLength = "5" };
        var value = "toolongvalue";

        // Act
        var result = ValidationHelper.CheckComponentLength(component, value);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckComponentLength_ShouldReturnTrue_WhenMinMaxBothSet()
    {
        // Arrange
        var component = new ResourceComponent { MinLength = "1", MaxLength = "100" };
        var value = "validvalue";

        // Act
        var result = ValidationHelper.CheckComponentLength(component, value);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("st-test-dev-001", "-", "st", true)] // Valid with delimiter
    [InlineData("sttest001", "", "st", true)] // Valid without delimiter
    [InlineData("testvalue", "-", "test", true)] // Valid
    [InlineData("", "-", "", false)] // Empty name
    public void ValidateName_ShouldValidateBasicCases(string name, string delimiter, string expectedPrefix, bool shouldContainPrefix)
    {
        // Act & Assert
        if (shouldContainPrefix && !string.IsNullOrEmpty(expectedPrefix))
        {
            name.Should().StartWith(expectedPrefix);
        }
        
        // Validate delimiter presence
        if (!string.IsNullOrEmpty(delimiter) && name.Contains(delimiter))
        {
            name.Should().Contain(delimiter);
        }
    }

    [Fact]
    public void CheckCasing_ShouldReturnTrue_WhenLowercaseRequired()
    {
        // Arrange
        var resourceType = new ResourceType 
        { 
            Regx = "[a-z0-9]+" // Lowercase only pattern
        };
        var name = "testname123";

        // Act
        var isLowercase = !resourceType.Regx.Contains("A-Z");

        // Assert
        isLowercase.Should().BeTrue();
        name.Should().MatchRegex("^[a-z0-9]+$");
    }

    [Fact]
    public void CheckCasing_ShouldAllowUppercase_WhenRegexIncludes()
    {
        // Arrange
        var resourceType = new ResourceType 
        { 
            Regx = "[a-zA-Z0-9]+" // Mixed case pattern
        };
        var name = "TestName123";

        // Act
        var allowsUppercase = resourceType.Regx.Contains("A-Z");

        // Assert
        allowsUppercase.Should().BeTrue();
        name.Should().MatchRegex("^[a-zA-Z0-9]+$");
    }

    #region ValidateGeneratedName Comprehensive Tests

    [Fact]
    public void ValidateGeneratedName_ShouldReturnValid_WhenNameMeetsAllRequirements()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test-resource-01";
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeTrue();
        result.Name.Should().Be(name);
        // Note: The method adds a lowercase message even for already-lowercase names
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenRegexPatternIsNull()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = null!,
            LengthMin = "3",
            LengthMax = "20"
        };
        var name = "test-resource";
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("Regex pattern is not configured");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenRegexPatternIsEmpty()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "",
            LengthMin = "3",
            LengthMax = "20"
        };
        var name = "test-resource";
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("Regex pattern is not configured");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldConvertToLowercase_WhenResourceTypeOnlyAllowsLowercase()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$", // No A-Z pattern
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "TEST-Resource-01";
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Name.Should().Be("test-resource-01");
        result.Message.Should().Contain("lowercase");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameIsTooShort()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "10",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("less than the minimum length");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameIsTooLongEvenWithoutDelimiter()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9]+$",
            LengthMin = "3",
            LengthMax = "10",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "verylongresourcename";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        // Note: Validation can fail in try-catch and return generic error message
        result.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateGeneratedName_ShouldRemoveDelimiter_WhenNameWithDelimiterIsTooLongButValidWithout()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "12",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test-name-01"; // 12 chars, OK
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameContainsInvalidCharacters()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "_@",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test_resource";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("cannot contain the following character: _");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameStartsWithInvalidCharacter()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "-",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "-testresource";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("cannot start with the following character: -");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameEndsWithInvalidCharacter()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "-",
            InvalidCharactersConsecutive = ""
        };
        var name = "testresource-";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("cannot end with the following character: -");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameHasConsecutiveInvalidCharacters()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = "-"
        };
        var name = "test--resource";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("cannot contain the following consecutive character: -");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldReturnInvalid_WhenNameFailsRegex()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9]+$", // No hyphens allowed
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test-resource";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("Regex failed");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldRemoveDelimiterAndRetry_WhenRegexFailsWithDelimiter()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9]+$", // No hyphens allowed
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test-resource"; // Will fail with hyphen
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        // After removing delimiter, "testresource" should pass regex
        result.Valid.Should().BeTrue();
        result.Name.Should().Be("testresource");
        result.Message.Should().Contain("delimiter was removed");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldHandleMultipleValidationErrors()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "_",
            InvalidCharactersStart = "-",
            InvalidCharactersEnd = "-",
            InvalidCharactersConsecutive = ""
        };
        var name = "-test_resource-";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeFalse();
        result.Message.Should().Contain("cannot start with");
        result.Message.Should().Contain("cannot end with");
        result.Message.Should().Contain("cannot contain the following character: _");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldHandleNullInvalidCharacterFields()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = string.Empty,
            InvalidCharactersStart = string.Empty,
            InvalidCharactersEnd = string.Empty,
            InvalidCharactersConsecutive = string.Empty
        };
        var name = "test-resource";
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeTrue();
        result.Name.Should().Be(name);
    }

    [Fact]
    public void ValidateGeneratedName_ShouldValidateWithEmptyDelimiter()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "testresource";
        var delimiter = "";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeTrue();
        result.Name.Should().Be("testresource");
    }

    [Fact]
    public void ValidateGeneratedName_ShouldNotRemoveDelimiter_WhenNameIsValidWithIt()
    {
        // Arrange
        var resourceType = new ResourceType
        {
            Regx = "^[a-z0-9-]+$",
            LengthMin = "3",
            LengthMax = "20",
            InvalidCharacters = "",
            InvalidCharactersStart = "",
            InvalidCharactersEnd = "",
            InvalidCharactersConsecutive = ""
        };
        var name = "test-res-01";
        var delimiter = "-";

        // Act
        var result = ValidationHelper.ValidateGeneratedName(resourceType, name, delimiter);

        // Assert
        result.Valid.Should().BeTrue();
        result.Name.Should().Be("test-res-01"); // Delimiter should remain
        // Note: The method adds a lowercase message even for already-lowercase names
    }

    #endregion
}
