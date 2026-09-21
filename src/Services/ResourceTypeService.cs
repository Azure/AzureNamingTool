#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using AzureNamingTool.Helpers;
using AzureNamingTool.Models;
using AzureNamingTool.Repositories;
using AzureNamingTool.Repositories.Interfaces;
using AzureNamingTool.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using ResourceType = AzureNamingTool.Models.ResourceType;

namespace AzureNamingTool.Services
{
    /// <summary>
    /// Service for managing Resource Types
    /// </summary>
    public class ResourceTypeService : IResourceTypeService
    {
        private readonly IConfigurationRepository<ResourceType> _repository;
        private readonly IAdminLogService _adminLogService;
        private readonly IResourceConfigurationCoordinator _coordinator;
        private readonly IResourceDelimiterService _resourceDelimiterService;

        public ResourceTypeService(
            IConfigurationRepository<ResourceType> repository,
            IAdminLogService adminLogService,
            IResourceConfigurationCoordinator coordinator,
            IResourceDelimiterService resourceDelimiterService)
        {
            _repository = repository;
            _adminLogService = adminLogService;
            _coordinator = coordinator;
            _resourceDelimiterService = resourceDelimiterService;
        }

        /// <summary>
        /// Retrieves a list of resource types.
        /// </summary>
        /// <param name="admin">Flag indicating whether to include all resource types or only enabled ones for admin.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response contains the list of resource types.</returns>
        public async Task<ServiceResponse> GetItemsAsync(bool admin = true)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                // Get list of items
                var items = (await _repository.GetAllAsync()).ToList();
                if (GeneralHelper.IsNotNull(items))
                {
                    if (!admin)
                    {
                        serviceResponse.ResponseObject = items.Where(x => x.Enabled == true).OrderBy(x => x.Resource).ToList();
                    }
                    else
                    {
                        serviceResponse.ResponseObject = items.OrderBy(x => x.Resource).ToList();
                    }
                    serviceResponse.Success = true;
                }
                else
                {
                    serviceResponse.ResponseObject = "Resource Types not found!";
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.Success = false;
                serviceResponse.ResponseObject = ex;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Retrieves a resource type by its ID.
        /// </summary>
        /// <param name="id">The ID of the resource type.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response contains the resource type.</returns>
        public async Task<ServiceResponse> GetItemAsync(int id)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                // Get list of items
                var items = (await _repository.GetAllAsync()).ToList();
                if (GeneralHelper.IsNotNull(items))
                {
                    var item = items.Find(x => x.Id == id);
                    if (GeneralHelper.IsNotNull(item))
                    {
                        serviceResponse.ResponseObject = item;
                        serviceResponse.Success = true;
                    }
                    else
                    {
                        serviceResponse.ResponseObject = "Resource Type not found!";
                    }
                }
                else
                {
                    serviceResponse.ResponseObject = "Resource Types not found!";
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.Success = false;
                serviceResponse.ResponseObject = ex;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Adds or updates a resource type.
        /// </summary>
        /// <param name="item">The resource type to add or update.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response contains the result of the operation.</returns>
        public async Task<ServiceResponse> PostItemAsync(ResourceType item)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                // Make sure the new item short name only contains letters/numbers
                if (!ValidationHelper.CheckAlphanumeric(item.ShortName))
                {
                    serviceResponse.Success = false;
                    serviceResponse.ResponseObject = "Short name must be alphanumeric.";
                    return serviceResponse;
                }

                // Get list of items
                var items = (await _repository.GetAllAsync()).ToList();
                if (GeneralHelper.IsNotNull(items))
                {
                    // Set the new id
                    if (item.Id == 0)
                    {
                        // Use max ID + 1 instead of count + 1 to avoid ID collisions after deletions
                        item.Id = items.Count > 0 ? items.Max(x => x.Id) + 1 : 1;
                    }

                    // Determine new item id
                    if (items.Count > 0)
                    {
                        // Check if the item already exists
                        if (items.Exists(x => x.Id == item.Id))
                        {
                            // Remove the updated item from the list
                            var existingitem = items.Find(x => x.Id == item.Id);
                            if (GeneralHelper.IsNotNull(existingitem))
                            {
                                int index = items.IndexOf(existingitem);
                                items.RemoveAt(index);
                            }
                        }

                        // Put the item at the end
                        items.Add(item);
                    }
                    else
                    {
                        item.Id = 1;
                        items.Add(item);
                    }

                    // Write items to file
                    await _repository.SaveAllAsync([.. items.OrderBy(x => x.Id)]);
                    // Get the item
                    var newitem = (await GetItemAsync((int)item.Id)).ResponseObject;
                    serviceResponse.ResponseObject = newitem;
                    serviceResponse.Success = true;
                }
                else
                {
                    serviceResponse.ResponseObject = "Resource Types not found!";
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.ResponseObject = ex;
                serviceResponse.Success = false;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Deletes a resource type by its ID.
        /// </summary>
        /// <param name="id">The ID of the resource type to delete.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response indicates the success of the operation.</returns>
        public async Task<ServiceResponse> DeleteItemAsync(int id)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                // Get list of items
                var items = (await _repository.GetAllAsync()).ToList();
                if (GeneralHelper.IsNotNull(items))
                {
                    // Get the specified item
                    var item = items.Find(x => x.Id == id);
                    if (GeneralHelper.IsNotNull(item))
                    {
                        // Remove the item from the collection
                        items.Remove(item);

                        // Write items to file
                        await _repository.SaveAllAsync(items);
                        serviceResponse.Success = true;
                    }
                    else
                    {
                        serviceResponse.ResponseObject = "Resource Type not found!";
                    }
                }
                else
                {
                    serviceResponse.ResponseObject = "Resource Types not found!";
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.ResponseObject = ex;
                serviceResponse.Success = false;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Adds or updates a resource type.
        /// </summary>
        /// <param name="items">The resource type to add or update.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response contains the result of the operation.</returns>
        public async Task<ServiceResponse> PostConfigAsync(List<ResourceType> items)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                // Get list of items
                var newitems = new List<ResourceType>();
                int i = 1;

                // Determine new item id
                foreach (ResourceType item in items)
                {
                    item.Id = i;
                    newitems.Add(item);
                    i += 1;
                }

                // Write items to file
                await _repository.SaveAllAsync(newitems);
                serviceResponse.Success = true;
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.Success = false;
                serviceResponse.ResponseObject = ex;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Retrieves the categories of resource types.
        /// </summary>
        /// <param name="types">The list of resource types.</param>
        /// <returns>A list of resource type categories.</returns>
        public List<string> GetTypeCategories(List<ResourceType> types)
        {
            ServiceResponse serviceResponse = new();
            List<string> categories = [];

            if (types == null)
            {
                return categories;
            }

            foreach (ResourceType type in types)
            {
                if (type == null) continue;

                string category = type.Resource;
                if (!String.IsNullOrEmpty(category))
                {
                    if (category.Contains('/'))
                    {
                        category = category[..category.IndexOf('/')];
                    }

                    if (!categories.Contains(category))
                    {
                        categories.Add(category);
                    }
                }
                else
                {
                    serviceResponse.ResponseObject = "Resource Type Categories not found!";
                }
            }

            return categories;
        }

        /// <summary>
        /// Retrieves a filtered list of resource types based on the specified filter.
        /// </summary>
        /// <param name="types">The list of resource types.</param>
        /// <param name="filter">The filter to apply.</param>
        /// <returns>The filtered list of resource types.</returns>
        public List<ResourceType> GetFilteredResourceTypes(List<ResourceType> types, string filter)
        {
            ServiceResponse serviceResponse = new();
            List<ResourceType> currenttypes = [];
            // Filter out resource types that should have name generation
            if (!String.IsNullOrEmpty(filter))
            {
                currenttypes = types.Where(x => x != null && x.Resource != null && x.Resource.StartsWith(filter + "/", StringComparison.CurrentCultureIgnoreCase) && !x.Property.Equals("display name", StringComparison.CurrentCultureIgnoreCase) && !String.IsNullOrEmpty(x.ShortName)).ToList();
            }
            else
            {
                currenttypes = types.Where(x => x != null).ToList();
            }
            return currenttypes;
        }

        /// <summary>
        /// Refreshes the resource types by retrieving the latest data from the repository.
        /// </summary>
        /// <param name="shortNameReset">Flag indicating whether to reset the short names of existing resource types.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response indicates the success of the operation.</returns>
        public async Task<ServiceResponse> RefreshResourceTypesAsync(bool shortNameReset = false)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                // Get the existing Resource Type items
                serviceResponse = await GetItemsAsync();
                if (serviceResponse.Success)
                {
                    List<ResourceType> types = (List<ResourceType>)serviceResponse.ResponseObject!;
                    if (GeneralHelper.IsNotNull(types))
                    {
                        string url = "https://raw.githubusercontent.com/mspnp/AzureNamingTool/main/src/repository/resourcetypes.json";
                        string refreshdata = await GeneralHelper.DownloadString(url);
                        if (!String.IsNullOrEmpty(refreshdata))
                        {
                            var newtypes = new List<ResourceType>();
                            JsonSerializerOptions options = new()
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                PropertyNameCaseInsensitive = true
                            };

                            newtypes = JsonSerializer.Deserialize<List<ResourceType>>(refreshdata, options);
                            if (GeneralHelper.IsNotNull(newtypes))
                            {
                                // Loop through the new items
                                // Add any new resource type and update any existing types
                                foreach (ResourceType newtype in newtypes)
                                {
                                    // Check if the existing types contain the current type
                                    int i = types.FindIndex(x => x.Resource == newtype.Resource);
                                    if (i > -1)
                                    {
                                        // Update the Resource Type Information
                                        ResourceType oldtype = types[i];
                                        newtype.Exclude = oldtype.Exclude;
                                        newtype.Optional = oldtype.Optional;
                                        newtype.Enabled = oldtype.Enabled;
                                        if ((!shortNameReset) || (String.IsNullOrEmpty(oldtype.ShortName)))
                                        {
                                            newtype.ShortName = oldtype.ShortName;
                                        }
                                        // Remove the old type
                                        types.RemoveAt(i);
                                        // Add the new type
                                        types.Add(newtype);
                                    }
                                    else
                                    {
                                        // Add a new resource type
                                        types.Add(newtype);
                                    }
                                }

                                // Update the settings file
                                serviceResponse = await PostConfigAsync(types);

                                // Update the repository file
                                await FileSystemHelper.WriteFile("resourcetypes.json", refreshdata, "repository/");

                                // Clear cached data
                                CacheHelper.InvalidateCacheObject("ResourceType");

                                // Update the current configuration file version data information
                                await ConfigurationHelper.UpdateConfigurationFileVersion("resourcetypes");
                            }
                            else
                            {
                                serviceResponse.ResponseObject = "Resource Types not found!";
                            }
                        }
                        else
                        {
                            serviceResponse.ResponseObject = "Refresh Resource Types not found!";
                            await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = "There was a problem refreshing the resource types configuration." });
                        }
                    }
                    else
                    {
                        serviceResponse.ResponseObject = "Resource Types not found!";
                    }
                }
                else
                {
                    serviceResponse.ResponseObject = "Resource Types not found!";
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.Success = false;
                serviceResponse.ResponseObject = ex;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Updates the components of a resource type based on the specified operation and component ID.
        /// </summary>
        /// <param name="operation">The operation to perform on the resource type components.</param>
        /// <param name="componentid">The ID of the component to update.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response indicates the success of the operation.</returns>
        public async Task<ServiceResponse> UpdateTypeComponentsAsync(string operation, int componentid)
        {
            ServiceResponse serviceResponse = new();
            try
            {
                serviceResponse = await _coordinator.GetComponentForTypeValidationAsync(componentid);
                if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                {
                    ResourceComponent resourceComponent = (ResourceComponent)serviceResponse.ResponseObject!;
                    if (GeneralHelper.IsNotNull(resourceComponent))
                    {
                        string component = GeneralHelper.NormalizeName(resourceComponent.Name, false);
                        serviceResponse = await GetItemsAsync();
                        if (serviceResponse.Success)
                        {
                            if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                            {
                                List<ResourceType> resourceTypes = (List<ResourceType>)serviceResponse.ResponseObject!;
                                if (GeneralHelper.IsNotNull(resourceTypes))
                                {
                                    bool hasUpdates = false;

                                    static List<string> ParseComponentValues(string? values)
                                    {
                                        return [.. (values ?? string.Empty)
                                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                                            .Select(x => x.Trim())
                                            .Where(x => !string.IsNullOrWhiteSpace(x))];
                                    }

                                    // Update all the resource type component settings
                                    foreach (ResourceType currenttype in resourceTypes)
                                    {
                                        switch (operation)
                                        {
                                            case "optional-add":
                                                var optionalAddValues = ParseComponentValues(currenttype.Optional);
                                                if (!optionalAddValues.Contains(component))
                                                {
                                                    optionalAddValues.Add(component);
                                                    currenttype.Optional = String.Join(",", [.. optionalAddValues]);
                                                    hasUpdates = true;
                                                }
                                                break;
                                            case "optional-remove":
                                                var optionalRemoveValues = ParseComponentValues(currenttype.Optional);
                                                if (optionalRemoveValues.Remove(component))
                                                {
                                                    currenttype.Optional = String.Join(",", [.. optionalRemoveValues]);
                                                    hasUpdates = true;
                                                }
                                                break;
                                            case "exclude-add":
                                                var excludeAddValues = ParseComponentValues(currenttype.Exclude);
                                                if (!excludeAddValues.Contains(component))
                                                {
                                                    excludeAddValues.Add(component);
                                                    currenttype.Exclude = String.Join(",", [.. excludeAddValues]);
                                                    hasUpdates = true;
                                                }
                                                break;
                                            case "exclude-remove":
                                                var excludeRemoveValues = ParseComponentValues(currenttype.Exclude);
                                                if (excludeRemoveValues.Remove(component))
                                                {
                                                    currenttype.Exclude = String.Join(",", [.. excludeRemoveValues]);
                                                    hasUpdates = true;
                                                }
                                                break;
                                        }
                                    }

                                    if (hasUpdates)
                                    {
                                        await _repository.SaveAllAsync([.. resourceTypes.OrderBy(x => x.Id)]);
                                    }

                                    serviceResponse.ResponseObject = hasUpdates
                                        ? "Resource Types updated!"
                                        : "Resource Types already up to date.";
                                    serviceResponse.Success = true;
                                }
                                else
                                {
                                    serviceResponse.ResponseObject = "Resource Types not found!";
                                }
                            }
                        }
                        else
                        {
                            serviceResponse.ResponseObject = "Resource Types not found!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.Success = false;
                serviceResponse.ResponseObject = ex;
            }
            return serviceResponse;
        }

        /// <summary>
        /// Validates the name of a resource type.
        /// </summary>
        /// <param name="validateNameRequest">The validate name request containing the resource type information.</param>
        /// <returns>A <see cref="Task{ServiceResponse}"/> representing the asynchronous operation. The service response contains the validation result.</returns>
        public async Task<ServiceResponse> ValidateResourceTypeNameAsync(ValidateNameRequest validateNameRequest)
        {
            ServiceResponse serviceResponse = new();
            ValidateNameResponse validateNameResponse = new();
            try
            {
                ResourceDelimiter? resourceDelimiter = new();
                // Get the current delimiter
                serviceResponse = await _resourceDelimiterService.GetCurrentItemAsync();
                if (serviceResponse.Success)
                {
                    if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        resourceDelimiter = serviceResponse.ResponseObject as ResourceDelimiter;
                    }
                }
                else
                {
                    serviceResponse.ResponseObject = "Delimiter value could not be set.";
                    serviceResponse.Success = false;
                    return serviceResponse;
                }

                // Get the specifed resource type
                serviceResponse = await GetItemsAsync(true);
                if (serviceResponse.Success)
                {
                    if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        // Get the resource types
                        List<ResourceType> resourceTypes = (List<ResourceType>)serviceResponse.ResponseObject!;
                        if (GeneralHelper.IsNotNull(resourceTypes))
                        {
                            ResourceType? resourceType = null;
                            if (GeneralHelper.IsNotNull(validateNameRequest.ResourceTypeId))
                            {
                                // Get the specified Resource type by id
                                resourceType = resourceTypes.FirstOrDefault(x => x.Id == validateNameRequest.ResourceTypeId)!;
                            }
                            else
                            {
                                // Get the specified Resource type by short name
                                resourceType = resourceTypes.FirstOrDefault(x => x.ShortName == validateNameRequest.ResourceType)!;
                            }
                            if (GeneralHelper.IsNotNull(resourceType))
                            {
                                // Create a validate name request
                                validateNameResponse = ValidationHelper.ValidateGeneratedName(resourceType, validateNameRequest.Name!, resourceDelimiter!.Delimiter);
                            }
                            else
                            {
                                validateNameResponse.Message = "Resoruce Type is invalid!";
                                validateNameResponse.Valid = false;
                            }
                        }
                        else
                        {
                            validateNameResponse.Message = "Resoruce Type is invalid!";
                            validateNameResponse.Valid = false;
                        }
                    }
                    else
                    {
                        validateNameResponse.Message = "Resoruce Type is invalid!";
                        validateNameResponse.Valid = false;
                    }
                }
                serviceResponse.ResponseObject = validateNameResponse;
                serviceResponse.Success = true;
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                serviceResponse.Success = false;
                serviceResponse.ResponseObject = ex;
            }
            return serviceResponse;
        }
    }
}

#pragma warning restore CS1591