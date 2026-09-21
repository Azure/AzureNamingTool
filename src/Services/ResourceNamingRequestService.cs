#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using AzureNamingTool.Helpers;
using AzureNamingTool.Models;
using AzureNamingTool.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Net.Mime.MediaTypeNames;

namespace AzureNamingTool.Services
{
    /// <summary>
    /// Service for managing Resource Naming Requests
    /// </summary>
    public class ResourceNamingRequestService : IResourceNamingRequestService
    {
        private readonly IResourceComponentService _resourceComponentService;
        private readonly IResourceDelimiterService _resourceDelimiterService;
        private readonly IResourceTypeService _resourceTypeService;
        private readonly IResourceEnvironmentService _resourceEnvironmentService;
        private readonly IResourceLocationService _resourceLocationService;
        private readonly IResourceOrgService _resourceOrgService;
        private readonly IResourceProjAppSvcService _resourceProjAppSvcService;
        private readonly IResourceUnitDeptService _resourceUnitDeptService;
        private readonly IResourceFunctionService _resourceFunctionService;
        private readonly ICustomComponentService _customComponentService;
        private readonly IGeneratedNamesService _generatedNamesService;
        private readonly IAdminLogService _adminLogService;
        private readonly IAzureValidationService? _azureValidationService;
        private readonly ConflictResolutionService? _conflictResolutionService;

        public ResourceNamingRequestService(
            IResourceComponentService resourceComponentService,
            IResourceDelimiterService resourceDelimiterService,
            IResourceTypeService resourceTypeService,
            IResourceEnvironmentService resourceEnvironmentService,
            IResourceLocationService resourceLocationService,
            IResourceOrgService resourceOrgService,
            IResourceProjAppSvcService resourceProjAppSvcService,
            IResourceUnitDeptService resourceUnitDeptService,
            IResourceFunctionService resourceFunctionService,
            ICustomComponentService customComponentService,
            IGeneratedNamesService generatedNamesService,
            IAdminLogService adminLogService,
            IAzureValidationService? azureValidationService = null,
            ConflictResolutionService? conflictResolutionService = null)
        {
            _resourceComponentService = resourceComponentService;
            _resourceDelimiterService = resourceDelimiterService;
            _resourceTypeService = resourceTypeService;
            _resourceEnvironmentService = resourceEnvironmentService;
            _resourceLocationService = resourceLocationService;
            _resourceOrgService = resourceOrgService;
            _resourceProjAppSvcService = resourceProjAppSvcService;
            _resourceUnitDeptService = resourceUnitDeptService;
            _resourceFunctionService = resourceFunctionService;
            _customComponentService = customComponentService;
            _generatedNamesService = generatedNamesService;
            _adminLogService = adminLogService;
            _azureValidationService = azureValidationService;
            _conflictResolutionService = conflictResolutionService;
        }

        /// <summary>
        /// This function will generate a Resource type name for specifed component values. This function requires full definition for all components. It is recommended to use the ResourceNameRequest API function for name generation.   
        /// </summary>
        /// <param name="request"></param>
        /// <returns>ResourceNameResponse - Response of name generation</returns>
        public async Task<ResourceNameResponse> RequestNameWithComponentsAsync(ResourceNameRequestWithComponents request)
        {
            ServiceResponse serviceResponse = new();
            ResourceNameResponse response = new()
            {
                Success = false
            };

            try
            {
                bool valid = true;
                bool ignoredelimeter = false;
                List<string[]> lstComponents = [];
                bool previousdelimiterappliedafter = true;

                // Get the specified resource type
                //var resourceTypes = await ConfigurationHelper.GetList<ResourceType>();
                //var resourceType = resourceTypes.Find(x => x.Id == request.ResourceType);
                var resourceType = request.ResourceType;

                // Check static value
                if (!String.IsNullOrEmpty(resourceType.StaticValues))
                {
                    // Return the static value and message and stop generation.
                    response.ResourceName = resourceType.StaticValues;
                    response.Message = "The requested Resource Type name is considered a static value with specific requirements. Please refer to https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules for additional information.";
                    response.Success = true;
                    return response;
                }

                // Get the components
                serviceResponse = await _resourceComponentService.GetItemsAsync(false);
                var currentResourceComponents = serviceResponse.ResponseObject;
                dynamic d = request;

                string name = "";

                StringBuilder sbMessage = new();

                // Loop through each component
                if (GeneralHelper.IsNotNull(currentResourceComponents))
                {
                    foreach (var component in currentResourceComponents!)
                    {
                        string normalizedcomponentname = GeneralHelper.NormalizeName(component.Name, true);
                        // Check if the component is excluded for the Resource Type
                        if (!resourceType.Exclude.ToLower().Split(',').Contains(normalizedcomponentname))
                        {
                            // Attempt to retrieve value from JSON body
                            var prop = GeneralHelper.GetPropertyValue(d, component.Name);
                            string value = String.Empty;

                            // Add property value to name, if exists
                            if (GeneralHelper.IsNotNull(prop))
                            {
                                if (component.Name == "ResourceInstance")
                                {
                                    value = prop;
                                }
                                else
                                {
                                    value = prop.GetType().GetProperty("ShortName").GetValue(prop, null);
                                }

                                // Check if the delimeter is already ignored
                                if (!ignoredelimeter)
                                {
                                    // Check if delimeter is an invalid character
                                    if (!String.IsNullOrEmpty(resourceType.InvalidCharacters))
                                    {
                                        if (!resourceType.InvalidCharacters.Contains(request.ResourceDelimiter.Delimiter))
                                        {
                                            if (!String.IsNullOrEmpty(name))
                                            {
                                                // Check if the component should apply the delimiter
                                                if ((component.ApplyDelimiterBefore) && (previousdelimiterappliedafter))
                                                {
                                                    name += request.ResourceDelimiter.Delimiter;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Add message about delimeter not applied
                                            sbMessage.Append("The specified delimiter is not allowed for this resource type and has been removed.");
                                            sbMessage.Append(Environment.NewLine);
                                            ignoredelimeter = true;
                                        }
                                    }
                                    else
                                    {
                                        // Deliemeter is valid so add it
                                        if (!String.IsNullOrEmpty(name))
                                        {
                                            // Check if the component should apply the delimiter
                                            if ((component.ApplyDelimiterBefore) && (previousdelimiterappliedafter))
                                            {
                                                name += request.ResourceDelimiter.Delimiter;
                                            }
                                        }
                                    }
                                }

                                name += value;

                                // Add property to aray for indivudal component validation
                                if (component.Name == "ResourceType")
                                {
                                    lstComponents.Add([component.Name, prop.Resource + " (" + value + ")"]);
                                }
                                else
                                {
                                    if (component.Name == "ResourceInstance")
                                    {
                                        lstComponents.Add([component.Name, prop]);
                                    }
                                    else
                                    {
                                        lstComponents.Add([component.Name, prop.Name + " (" + value + ")"]);
                                    }
                                }
                            }
                            else
                            {
                                // Check if the prop is optional
                                if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                {
                                    valid = false;
                                    break;
                                }
                            }
                        }
                        previousdelimiterappliedafter = component.ApplyDelimiterAfter;
                    }
                }
                // Check if the required component were supplied
                if (!valid)
                {
                    response.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                    response.Message = "You must supply the required components.";
                    return response;
                }

                // Check the Resource Instance value to ensure it's only nmumeric
                if (GeneralHelper.IsNotNull(lstComponents.FirstOrDefault(x => x[0] == "ResourceInstance")))
                {
                    if (GeneralHelper.IsNotNull(lstComponents.FirstOrDefault(x => x[0] == "ResourceInstance")![1]))
                    {
                        if (!ValidationHelper.CheckNumeric(lstComponents.FirstOrDefault(x => x[0] == "ResourceInstance")![1]))
                        {
                            sbMessage.Append("Resource Instance must be a numeric value.");
                            sbMessage.Append(Environment.NewLine);
                            valid = false;
                        }
                    }
                }

                // Validate the generated name for the resource type
                // CALL VALIDATION FUNCTION
                ValidateNameRequest validateNameRequest = new()
                {
                    ResourceTypeId = resourceType.Id,
                    ResourceType = resourceType.ShortName,
                    Name = name
                };
                serviceResponse = await _resourceTypeService.ValidateResourceTypeNameAsync(validateNameRequest);
                if (serviceResponse.Success)
                {
                    if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        ValidateNameResponse validateNameResponse = (ValidateNameResponse)serviceResponse.ResponseObject!;
                        valid = validateNameResponse.Valid;
                        if (!String.IsNullOrEmpty(validateNameResponse.Name))
                        {
                            name = validateNameResponse.Name;
                        }
                        if (!String.IsNullOrEmpty(validateNameResponse.Message))
                        {
                            sbMessage.Append(validateNameResponse.Message);
                        }
                    }
                }

                if (valid)
                {
                    GeneratedName generatedName = new()
                    {
                        CreatedOn = DateTime.Now,
                        ResourceName = name,
                        Components = lstComponents
                    };
                    await _generatedNamesService.PostItemAsync(generatedName);
                    response.Success = true;
                    response.ResourceName = name;
                    response.Message = sbMessage.ToString();
                    return response;
                }
                else
                {
                    response.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                    response.Message = sbMessage.ToString();
                    return response;
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                response.Message = ex.Message;
                return response;
            }
        }

        /// <summary>
        /// This function is used to generate a name by providing each component and the short name value. The function will validate the values to ensure they match the current configuration. 
        /// </summary>
        /// <param name="request"></param>
        /// <returns>ResourceNameResponse - Response of name generation</returns>
        public async Task<ResourceNameResponse> RequestNameAsync(ResourceNameRequest request)
        {
            ResourceNameResponse resourceNameResponse = new()
            {
                Success = false
            };

            try
            {
                bool valid = true;
                bool ignoredelimeter = false;
                List<string[]> lstComponents = [];
                ServiceResponse serviceResponse = new();
                ResourceDelimiter resourceDelimiter = new();
                ResourceType resourceType = new();
                string name = "";
                int resourceInstanceStartIndex = -1;
                int resourceInstanceLength = 0;
                StringBuilder sbMessage = new();
                bool previousdelimiterappliedafter = true;

                // Get the current delimiter
                serviceResponse = await _resourceDelimiterService.GetCurrentItemAsync();
                if (serviceResponse.Success)
                {
                    resourceDelimiter = (ResourceDelimiter)serviceResponse.ResponseObject!;
                }
                else
                {
                    valid = false;
                    resourceNameResponse.Message = "Delimiter value could not be set.";
                    resourceNameResponse.Success = false;
                    return resourceNameResponse;
                }

                // Get the specified resource type - use service layer for SQLite/FileSystem compatibility
                serviceResponse = await _resourceTypeService.GetItemsAsync();
                List<ResourceType>? resourceTypes = null;
                if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                {
                    resourceTypes = (List<ResourceType>)serviceResponse.ResponseObject!;
                }
                
                if (GeneralHelper.IsNotNull(resourceTypes))
                {
                    var resourceTypesByShortName = resourceTypes.FindAll(x => x.ShortName == request.ResourceType);
                    if (!GeneralHelper.IsNotNull(resourceTypesByShortName))
                    {
                        valid = false;
                        resourceNameResponse.Message = "ResourceType value is invalid.";
                        resourceNameResponse.Success = false;
                        return resourceNameResponse;
                    }
                    else
                    {
                        if (resourceTypesByShortName.Count == 0)
                        {
                            valid = false;
                            resourceNameResponse.Message = "ResourceType value is invalid.";
                            resourceNameResponse.Success = false;
                            return resourceNameResponse;
                        }
                        // Check if there are duplicates
                        if (resourceTypesByShortName.Count > 1)
                        {
                            // Check that the request includes a resource name
                            if (request.ResourceId != 0)
                            {
                                // Check if the resource value is valid
                                resourceType = resourceTypesByShortName.Find(x => x.Id == request.ResourceId)!;
                                if (!GeneralHelper.IsNotNull(resourceType))
                                {
                                    valid = false;
                                    resourceNameResponse.Message = "Resource Id value is invalid.";
                                    resourceNameResponse.Success = false;
                                    return resourceNameResponse;
                                }
                            }
                            else
                            {
                                valid = false;
                                resourceNameResponse.Message = "Your configuration contains multiple resource types for the provided short name. You must supply the Resource Id value for the resource type in your request.(Example: resourceId: 14)";
                                resourceNameResponse.Success = false;
                                return resourceNameResponse;
                            }
                        }
                        else
                        {
                            // Set the resource type ot the first value
                            resourceType = resourceTypesByShortName[0];
                        }
                    }

                    // Check static value
                    if (!String.IsNullOrEmpty(resourceType.StaticValues))
                    {
                        // Return the static value and message and stop generation.
                        resourceNameResponse.ResourceName = resourceType.StaticValues;
                        resourceNameResponse.Message = "The requested Resource Type name is considered a static value with specific requirements. Please refer to https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules for additional information.";
                        resourceNameResponse.Success = true;
                        return resourceNameResponse;
                    }

                    // Load all component data once for validation (SQLite/FileSystem compatibility)
                    List<ResourceEnvironment>? environments = null;
                    serviceResponse = await _resourceEnvironmentService.GetItemsAsync();
                    if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        environments = (List<ResourceEnvironment>)serviceResponse.ResponseObject!;
                    }

                    List<ResourceLocation>? locations = null;
                    serviceResponse = await _resourceLocationService.GetItemsAsync();
                    if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        locations = (List<ResourceLocation>)serviceResponse.ResponseObject!;
                    }

                    List<ResourceOrg>? orgs = null;
                    serviceResponse = await _resourceOrgService.GetItemsAsync();
                    if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        orgs = (List<ResourceOrg>)serviceResponse.ResponseObject!;
                    }

                    List<ResourceProjAppSvc>? projappsvcs = null;
                    serviceResponse = await _resourceProjAppSvcService.GetItemsAsync();
                    if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        projappsvcs = (List<ResourceProjAppSvc>)serviceResponse.ResponseObject!;
                    }

                    List<ResourceUnitDept>? unitdepts = null;
                    serviceResponse = await _resourceUnitDeptService.GetItemsAsync();
                    if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        unitdepts = (List<ResourceUnitDept>)serviceResponse.ResponseObject!;
                    }

                    List<ResourceFunction>? functions = null;
                    serviceResponse = await _resourceFunctionService.GetItemsAsync();
                    if (serviceResponse.Success && GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        functions = (List<ResourceFunction>)serviceResponse.ResponseObject!;
                    }

                    // Make sure the passed custom component names are normalized
                    if (GeneralHelper.IsNotNull(request.CustomComponents))
                    {
                        Dictionary<string, string> newComponents = [];
                        foreach (var cc in request.CustomComponents)
                        {
                            string value = cc.Value;
                            newComponents.Add(GeneralHelper.NormalizeName(cc.Key, true), value);
                        }
                        request.CustomComponents = newComponents;
                    }

                    // Get the current components
                    serviceResponse = await _resourceComponentService.GetItemsAsync(false);
                    if (serviceResponse.Success)
                    {
                        if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                        {
                            var currentResourceComponents = serviceResponse.ResponseObject;
                            if (GeneralHelper.IsNotNull(currentResourceComponents))
                            {
                                // Loop through each component
                                foreach (var component in currentResourceComponents!)
                                {
                                    string normalizedcomponentname = GeneralHelper.NormalizeName(component.Name, true);
                                    if (!component.IsCustom)
                                    {
                                        // Check if the component is excluded for the Resource Type
                                        if (!resourceType.Exclude.ToLower().Split(',').Contains(normalizedcomponentname))
                                        {
                                            // Attempt to retrieve value from JSON body
                                            var value = GeneralHelper.GetPropertyValue(request, component.Name);

                                            // Add property value to name, if exists
                                            if (GeneralHelper.IsNotNull(value))
                                            {
                                                if (!String.IsNullOrEmpty(value))
                                                {
                                                    // Check of the component value length is valid
                                                    if (!ValidationHelper.CheckComponentLength(component, value))
                                                    {
                                                        sbMessage.Append(component.DisplayName + " value length is invalid. The value must be between " + component.MinLength + " and " + component.MaxLength + " characters.");
                                                        valid = false;
                                                    }

                                                    if (valid)
                                                    {
                                                        // Validate that the value is a valid option for the component
                                                        switch (component.Name.ToLower())
                                                        {
                                                            case "resourcetype":
                                                                if (GeneralHelper.IsNotNull(resourceTypes))
                                                                {
                                                                    var type = resourceTypes.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(type))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("ResourceType value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                            case "resourceenvironment":
                                                                if (GeneralHelper.IsNotNull(environments))
                                                                {
                                                                    var environment = environments.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(environment))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("ResourceEnvironment value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                            case "resourcelocation":
                                                                if (GeneralHelper.IsNotNull(locations))
                                                                {
                                                                    var location = locations.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(location))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("ResourceLocation value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                            case "resourceorg":
                                                                if (GeneralHelper.IsNotNull(orgs))
                                                                {
                                                                    var org = orgs.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(org))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("Resource Type value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                            case "resourceprojappsvc":
                                                                if (GeneralHelper.IsNotNull(projappsvcs))
                                                                {
                                                                    var projappsvc = projappsvcs.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(projappsvc))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("ResourceProjAppSvc value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                            case "resourceunitdept":
                                                                if (GeneralHelper.IsNotNull(unitdepts))
                                                                {
                                                                    var unitdept = unitdepts.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(unitdept))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("ResourceUnitDept value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                            case "resourcefunction":
                                                                if (GeneralHelper.IsNotNull(functions))
                                                                {
                                                                    var function = functions.Find(x => x.ShortName == value);
                                                                    if (!GeneralHelper.IsNotNull(function))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append("ResourceFunction value is invalid. ");
                                                                    }
                                                                }
                                                                break;
                                                        }

                                                        // Check if the delimiter is already ignored
                                                        if ((!ignoredelimeter) && (!String.IsNullOrEmpty(resourceDelimiter.Delimiter)))
                                                        {
                                                            // Check if delimiter is an invalid character
                                                            if (!String.IsNullOrEmpty(resourceType.InvalidCharacters))
                                                            {
                                                                if (!resourceType.InvalidCharacters.Contains(resourceDelimiter.Delimiter))
                                                                {
                                                                    if (name != "")
                                                                    {
                                                                        // Check if the component should apply the delimiter
                                                                        if ((component.ApplyDelimiterBefore) && (previousdelimiterappliedafter) && (resourceType.ApplyDelimiter))
                                                                        {
                                                                            name += resourceDelimiter.Delimiter;
                                                                        }
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    // Add message about delimeter not applied
                                                                    sbMessage.Append("The specified delimiter is not allowed for this resource type and has been removed. ");
                                                                    ignoredelimeter = true;
                                                                }
                                                            }
                                                            else
                                                            {
                                                                // Delimiter is valid so add it
                                                                if (!String.IsNullOrEmpty(name))
                                                                {
                                                                    // Check if the component should apply the delimiter
                                                                    if ((component.ApplyDelimiterBefore) && (previousdelimiterappliedafter) && (resourceType.ApplyDelimiter))
                                                                    {
                                                                        name += resourceDelimiter.Delimiter;
                                                                    }
                                                                }
                                                            }
                                                        }

                                                        if (component.Name == "ResourceInstance")
                                                        {
                                                            resourceInstanceStartIndex = name.Length;
                                                            resourceInstanceLength = value.Length;
                                                        }

                                                        name += value;

                                                        // Add property to array for individual component validation
                                                        if (!resourceType.Exclude.ToLower().Split(',').Contains(normalizedcomponentname))
                                                        {
                                                            lstComponents.Add([component.Name, value]);
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    // Check if the prop is optional
                                                    if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                    {
                                                        valid = false;
                                                        sbMessage.Append(component.Name + " value was not provided. ");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // Check if the prop is optional
                                                if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                {
                                                    valid = false;
                                                    sbMessage.Append(component.Name + " value was not provided. ");
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (!component.IsFreeText)
                                        {
                                            // Get the custom components data
                                            serviceResponse = await _customComponentService.GetItemsAsync();
                                            if (serviceResponse.Success)
                                            {
                                                if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                                                {
                                                    var customcomponents = (List<CustomComponent>)serviceResponse.ResponseObject!;
                                                    if (GeneralHelper.IsNotNull(customcomponents))
                                                    {
                                                        // Make sure the custom component has values
                                                        if (customcomponents.Where(x => x.ParentComponent == normalizedcomponentname).Any())
                                                        {
                                                            // Make sure the CustomComponents property was provided
                                                            if (!resourceType.Exclude.ToLower().Split(',').Contains(normalizedcomponentname))
                                                            {
                                                                // Add property value to name, if exists
                                                                if (GeneralHelper.IsNotNull(request.CustomComponents))
                                                                {
                                                                    // Check if the custom compoment value was provided in the request
                                                                    if (request.CustomComponents.TryGetValue(normalizedcomponentname, out string? value))
                                                                    {
                                                                        // Get the value from the provided custom components
                                                                        var componentvalue = value;
                                                                        if (!GeneralHelper.IsNotNull(componentvalue))
                                                                        {
                                                                            // Check if the prop is optional
                                                                            if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                                            {
                                                                                valid = false;
                                                                                sbMessage.Append(component.Name + " value was not provided. ");
                                                                            }
                                                                        }
                                                                        else
                                                                        {
                                                                            // Check if the custom component is empty
                                                                            if (!String.IsNullOrEmpty(componentvalue))
                                                                            {
                                                                                // Check to make sure it is a valid custom component
                                                                                // Use service layer instead of ConfigurationHelper to support both SQLite and FileSystem
                                                                                var customComponentsResponse = await _customComponentService.GetItemsAsync();
                                                                                if (customComponentsResponse.Success && GeneralHelper.IsNotNull(customComponentsResponse.ResponseObject))
                                                                                {
                                                                                    var customComponents = (List<CustomComponent>)customComponentsResponse.ResponseObject!;
                                                                                    var validcustomComponent = customComponents.Find(x => x.ParentComponent == normalizedcomponentname && x.ShortName == componentvalue);
                                                                                    if (!GeneralHelper.IsNotNull(validcustomComponent))
                                                                                    {
                                                                                        valid = false;
                                                                                        sbMessage.Append(component.Name + " value is not a valid custom component short name. ");
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        if (!String.IsNullOrEmpty(name) && (resourceType.ApplyDelimiter))
                                                                                        {
                                                                                            name += resourceDelimiter.Delimiter;
                                                                                        }

                                                                                        name += componentvalue;

                                                                                        // Add property to array for individual component validation
                                                                                        lstComponents.Add([component.Name, componentvalue]);
                                                                                    }
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                // Check if the prop is optional
                                                                                if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                                                {
                                                                                    valid = false;
                                                                                    sbMessage.Append(component.Name + " value was not provided. ");
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        // Check if the prop is optional
                                                                        if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                                        {
                                                                            valid = false;
                                                                            sbMessage.Append(component.Name + " value was not provided. ");
                                                                        }
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    // Check if the prop is optional
                                                                    if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                                    {
                                                                        valid = false;
                                                                        sbMessage.Append(component.Name + " value was not provided. ");
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }

                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Make sure the CustomComponents property was provided
                                            if (!resourceType.Exclude.ToLower().Split(',').Contains(normalizedcomponentname))
                                            {
                                                // Add property value to name, if exists
                                                if (GeneralHelper.IsNotNull(request.CustomComponents))
                                                {
                                                    // Check if the custom compoment value was provided in the request
                                                    if (request.CustomComponents.TryGetValue(normalizedcomponentname, out string? value))
                                                    {
                                                        // Get the value from the provided custom components
                                                        var componentvalue = value;
                                                        if (!GeneralHelper.IsNotNull(componentvalue))
                                                        {
                                                            // Check if the prop is optional
                                                            if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                            {
                                                                valid = false;
                                                                sbMessage.Append(component.Name + " value was not provided. ");
                                                            }
                                                        }
                                                        else
                                                        {
                                                            // CHeck if the free text has a value
                                                            if (!String.IsNullOrEmpty(componentvalue))
                                                            {
                                                                if (!String.IsNullOrEmpty(name))
                                                                {
                                                                    // Check if the component should apply the delimiter
                                                                    if ((component.ApplyDelimiterBefore) && (previousdelimiterappliedafter) && (resourceType.ApplyDelimiter))
                                                                    {
                                                                        name += resourceDelimiter.Delimiter;
                                                                    }
                                                                }

                                                                name += componentvalue;

                                                                // Add property to array for individual component validation
                                                                lstComponents.Add([component.Name, componentvalue]);
                                                            }
                                                            else
                                                            {
                                                                // Check if the prop is optional
                                                                if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                                {
                                                                    valid = false;
                                                                    sbMessage.Append(component.Name + " value was not provided. ");
                                                                }
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // Check if the prop is optional
                                                        if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                        {
                                                            valid = false;
                                                            sbMessage.Append(component.Name + " value was not provided. ");
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    // Check if the prop is optional
                                                    if (!resourceType.Optional.ToLower().Split(',').Contains(normalizedcomponentname))
                                                    {
                                                        valid = false;
                                                        sbMessage.Append(component.Name + " value was not provided. ");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // Set the previous apply deimiter flag
                                    previousdelimiterappliedafter = component.ApplyDelimiterAfter;
                                }
                            }
                        }
                    }
                }
                else
                {
                    valid = false;
                    sbMessage.Append("There was a problem generating the name.");
                }

                // Check if the required component were supplied
                if (!valid)
                {
                    resourceNameResponse.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                    resourceNameResponse.Message = "You must supply the required components. " + sbMessage.ToString();
                    return resourceNameResponse;
                }

                // Check the Resource Instance value to ensure it's only nmumeric
                if (GeneralHelper.IsNotNull(lstComponents))
                {
                    if (GeneralHelper.IsNotNull(lstComponents.FirstOrDefault(x => x[0] == "ResourceInstance")))
                    {
                        if (GeneralHelper.IsNotNull(lstComponents.FirstOrDefault(x => x[0] == "ResourceInstance")![1]))
                        {
                            if (!ValidationHelper.CheckNumeric(lstComponents.FirstOrDefault(x => x[0] == "ResourceInstance")![1]))
                            {
                                sbMessage.Append("Resource Instance must be a numeric value.");
                                sbMessage.Append(Environment.NewLine);
                                valid = false;
                            }
                        }
                    }
                }

                // Validate the generated name for the resource type
                ValidateNameRequest validateNameRequest = new()
                {
                    ResourceTypeId = resourceType.Id,
                    ResourceType = resourceType.ShortName,
                    Name = name
                };
                string nameBeforeValidation = name;
                serviceResponse = await _resourceTypeService.ValidateResourceTypeNameAsync(validateNameRequest);
                if (serviceResponse.Success)
                {
                    if (GeneralHelper.IsNotNull(serviceResponse.ResponseObject))
                    {
                        ValidateNameResponse validateNameResponse = (ValidateNameResponse)serviceResponse.ResponseObject!;
                        valid = validateNameResponse.Valid;
                        if (!String.IsNullOrEmpty(validateNameResponse.Name))
                        {
                            name = validateNameResponse.Name;
                            if (resourceInstanceStartIndex >= 0 && name.Length != nameBeforeValidation.Length)
                            {
                                string nameWithoutDelimiter = nameBeforeValidation.Replace(resourceDelimiter.Delimiter, "");
                                if (String.Equals(name, nameWithoutDelimiter, StringComparison.OrdinalIgnoreCase))
                                {
                                    int originalStartIndex = resourceInstanceStartIndex;
                                    resourceInstanceStartIndex = nameBeforeValidation[..originalStartIndex]
                                        .Replace(resourceDelimiter.Delimiter, "").Length;
                                    resourceInstanceLength = nameBeforeValidation
                                        .Substring(originalStartIndex, resourceInstanceLength)
                                        .Replace(resourceDelimiter.Delimiter, "").Length;
                                }
                                else
                                {
                                    resourceInstanceStartIndex = -1;
                                    resourceInstanceLength = 0;
                                }
                            }
                        }
                        if (!String.IsNullOrEmpty(validateNameResponse.Message))
                        {
                            sbMessage.Append(validateNameResponse.Message);
                        }
                    }
                }

                if (valid)
                {
                    bool nameallowed = true;
                    
                    // Internal duplicate handling is based on generated-name history and should
                    // run regardless of Azure tenant validation settings.
                    bool nameexists = await ConfigurationHelper.CheckIfGeneratedNameExists(name, _generatedNamesService);
                    if (nameexists)
                    {
                        // Check if Resource Instance was included in the generated name.
                        if (resourceInstanceStartIndex >= 0 && resourceInstanceLength > 0)
                        {
                            // Check if the name should be auto-incremented
                            if (Convert.ToBoolean(ConfigurationHelper.GetAppSetting("AutoIncrementResourceInstance")))
                            {
                                // Check if there was a ResourceInstance value supplied
                                if (GeneralHelper.IsNotNull(GeneralHelper.GetPropertyValue(request, "ResourceInstance")))
                                {
                                    // Attempt to auto-increement the instance 
                                    // Set the original name value
                                    string originalname = name;
                                    // Get the instance value
                                    string originalinstance = GeneralHelper.GetPropertyValue(request, "ResourceInstance")?.ToString() ?? "";
                                    // Determine the next instance value
                                    string newinstance = String.Empty;
                                    int i = 1;
                                    while (await ConfigurationHelper.CheckIfGeneratedNameExists(name, _generatedNamesService))
                                    {
                                        newinstance = (Convert.ToInt32(originalinstance) + i).ToString();
                                        // Make sure the instance pattern matches the entered values (leading zeros)
                                        while (newinstance.Length < originalinstance.Length)
                                        {
                                            newinstance = "0" + newinstance;
                                        }
                                        string incrementedName = ReplaceResourceInstance(
                                            originalname,
                                            resourceInstanceStartIndex,
                                            resourceInstanceLength,
                                            newinstance);
                                        serviceResponse = await _resourceTypeService.ValidateResourceTypeNameAsync(new ValidateNameRequest
                                        {
                                            ResourceTypeId = resourceType.Id,
                                            ResourceType = resourceType.ShortName,
                                            Name = incrementedName
                                        });
                                        ValidateNameResponse? incrementedNameResponse =
                                            serviceResponse.ResponseObject as ValidateNameResponse;
                                        if (!serviceResponse.Success || incrementedNameResponse is null || !incrementedNameResponse.Valid)
                                        {
                                            resourceNameResponse.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                                            resourceNameResponse.Message = incrementedNameResponse?.Message
                                                ?? "The auto-incremented resource name is invalid.";
                                            return resourceNameResponse;
                                        }
                                        name = String.IsNullOrEmpty(incrementedNameResponse.Name)
                                            ? incrementedName
                                            : incrementedNameResponse.Name;
                                        // Increase the counter
                                        i += 1;
                                    }
                                    sbMessage.Append("The resource instance has been auto-incremented to the next value.");
                                    sbMessage.Append(Environment.NewLine);
                                }
                                else
                                {
                                    // Check if duplicate names are allowed
                                    if (!Convert.ToBoolean(ConfigurationHelper.GetAppSetting("DuplicateNamesAllowed")))
                                    {
                                        nameallowed = false;
                                    }
                                }
                            }
                            else
                            {
                                // Check if duplicate names are allowed
                                if (!Convert.ToBoolean(ConfigurationHelper.GetAppSetting("DuplicateNamesAllowed")))
                                {
                                    nameallowed = false;
                                }
                            }
                        }
                        else
                        {
                            // Request does not contain an instance component
                            // Check if duplicate names are allowed
                            if (!Convert.ToBoolean(ConfigurationHelper.GetAppSetting("DuplicateNamesAllowed")))
                            {
                                nameallowed = false;
                            }
                        }
                    }

                    if (nameallowed)
                    {
                        GeneratedName generatedName = new()
                        {
                            CreatedOn = DateTime.Now,
                            ResourceName = name,
                            Components = lstComponents,
                            ResourceTypeName = resourceType.Resource,
                            User = request.CreatedBy,
                            Message = sbMessage.ToString()
                        };

                        // Check if the property should be appended to name
                        if (!String.IsNullOrEmpty(resourceType.Property))
                        {
                            generatedName.ResourceTypeName += " - " + resourceType.Property;
                        }

                        // Azure Validation and Conflict Resolution
                        AzureValidationMetadata? validationMetadata = null;
                        if (_azureValidationService != null && _conflictResolutionService != null)
                        {
                            try
                            {
                                // Check if Azure validation is enabled
                                var siteConfig = ConfigurationHelper.GetConfigurationData();
                                if (siteConfig != null && siteConfig.AzureTenantNameValidationEnabled?.Equals("True", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    // Get Azure validation settings
                                    var validationSettings = await _azureValidationService.GetSettingsAsync();
                                    if (validationSettings != null && validationSettings.Enabled)
                                    {
                                        // Validate the name against Azure
                                        validationMetadata = await _azureValidationService.ValidateNameAsync(
                                            name,
                                            resourceType.ShortName ?? resourceType.Resource
                                        );

                                        // If name exists in Azure, apply conflict resolution
                                        if (validationMetadata.ExistsInAzure)
                                        {
                                            var resolutionResult = await _conflictResolutionService.ResolveConflictAsync(
                                                name,
                                                resourceType.ShortName ?? resourceType.Resource,
                                                validationSettings
                                            );

                                            if (resolutionResult.Success)
                                            {
                                                // Update the name with the resolved name
                                                name = resolutionResult.FinalName;
                                                generatedName.ResourceName = name;

                                                // Update validation metadata with resolution info
                                                validationMetadata.OriginalName = resolutionResult.OriginalName;
                                                validationMetadata.IncrementAttempts = resolutionResult.Attempts;
                                                
                                                // Add resolution info to message
                                                string resolutionMessage = $"Name conflict resolved using {resolutionResult.Strategy} strategy. Original: {resolutionResult.OriginalName}, Final: {resolutionResult.FinalName}";
                                                if (!string.IsNullOrEmpty(resolutionResult.Warning))
                                                {
                                                    validationMetadata.ValidationWarning = resolutionResult.Warning;
                                                    resolutionMessage += $" Warning: {resolutionResult.Warning}";
                                                }
                                                
                                                if (sbMessage.Length > 0)
                                                    sbMessage.Append(" ");
                                                sbMessage.Append(resolutionMessage);
                                            }
                                            else
                                            {
                                                // Conflict resolution failed - return error
                                                resourceNameResponse.Success = false;
                                                resourceNameResponse.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                                                resourceNameResponse.Message = resolutionResult.ErrorMessage ?? "Failed to resolve naming conflict with Azure tenant.";
                                                resourceNameResponse.ValidationMetadata = validationMetadata;
                                                return resourceNameResponse;
                                            }
                                        }
                                        
                                        // Set validation timestamp
                                        validationMetadata.ValidationTimestamp = DateTime.UtcNow;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Log but don't fail name generation if Azure validation fails
                                await _adminLogService.PostItemAsync(new AdminLogMessage() 
                                { 
                                    Title = "WARNING", 
                                    Message = $"Azure validation failed: {ex.Message}" 
                                });
                                
                                // Create validation metadata indicating the error
                                validationMetadata = new AzureValidationMetadata
                                {
                                    ValidationPerformed = true,
                                    ValidationWarning = $"Azure validation could not be performed: {ex.Message}",
                                    ValidationTimestamp = DateTime.UtcNow
                                };
                            }
                        }

                        ServiceResponse responseGenerateName = await _generatedNamesService.PostItemAsync(generatedName);
                        if (responseGenerateName.Success)
                        {
                            resourceNameResponse.Success = true;
                            resourceNameResponse.ResourceName = name;
                            resourceNameResponse.Message = sbMessage.ToString();
                            resourceNameResponse.ResourceNameDetails = generatedName;
                            resourceNameResponse.ValidationMetadata = validationMetadata;


                            // Check if the GenerationWebhook is configured
                            String webhook = ConfigurationHelper.GetAppSetting("GenerationWebhook", true);
                            if (!String.IsNullOrEmpty(webhook))
                            {
                                // Asynchronously post to the webhook
                                await ConfigurationHelper.PostToGenerationWebhook(webhook, generatedName);
                            }
                        }
                        else
                        {
                            resourceNameResponse.Success = false;
                            resourceNameResponse.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                            resourceNameResponse.Message = "There was an error generating the name. Please try again.";
                        }
                    }
                    else
                    {
                        resourceNameResponse.Success = false;
                        resourceNameResponse.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                        resourceNameResponse.Message = "The name (" + name + ") you are trying to generate already exists. Please select different component options and try again.";
                    }
                    return resourceNameResponse;
                }
                else
                {
                    resourceNameResponse.ResourceName = "***RESOURCE NAME NOT GENERATED***";
                    resourceNameResponse.Message = sbMessage.ToString();
                    return resourceNameResponse;
                }
            }
            catch (Exception ex)
            {
                await _adminLogService.PostItemAsync(new AdminLogMessage() { Title = "ERROR", Message = ex.Message });
                resourceNameResponse.Message = ex.Message;
                return resourceNameResponse;
            }
        }

        internal static string ReplaceResourceInstance(
            string resourceName,
            int instanceStartIndex,
            int instanceLength,
            string replacement)
        {
            return resourceName.Remove(instanceStartIndex, instanceLength)
                .Insert(instanceStartIndex, replacement);
        }
    }
}

#pragma warning restore CS1591