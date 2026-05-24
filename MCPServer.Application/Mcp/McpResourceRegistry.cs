using System.Globalization;
using Autofac.Features.Indexed;
using LanguageExt.Common;
using LanguageExt;
using MCPServer.Application.Mcp.Interfaces;
using MCPServer.Domain.Mcp;

namespace MCPServer.Application.Mcp;

public sealed class McpResourceRegistry : IMcpResourceRegistry
{
    private readonly McpResourceDescriptor[] _descriptors;
    private readonly ResourcesListResult _allResourcesResult;
    private readonly ResourceTemplatesListResult _emptyTemplatesResult;
    private readonly IIndex<string, IMcpResource> _resourcesByUri;
    private readonly int _pageSize;

    public McpResourceRegistry(
        IEnumerable<IMcpResource> resources,
        IIndex<string, IMcpResource> resourcesByUri,
        McpResourceRegistryOptions options)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(resourcesByUri);
        ArgumentNullException.ThrowIfNull(options);

        var pageSize = Math.Clamp(options.ResourceListPageSize, 1, 1_024);
        var resourceArray = resources as IMcpResource[] ?? resources.ToArray();
        var descriptors = new McpResourceDescriptor[resourceArray.Length];
        var uris = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < resourceArray.Length; i++)
        {
            var descriptor = resourceArray[i].Descriptor;
            ValidateDescriptor(descriptor, uris);
            descriptors[i] = descriptor;
        }

        _descriptors = descriptors;
        _allResourcesResult = new ResourcesListResult { Resources = descriptors };
        _emptyTemplatesResult = new ResourceTemplatesListResult();
        _resourcesByUri = resourcesByUri;
        _pageSize = pageSize;
    }

    public Fin<ResourcesListResult> ListResources(string? cursor)
    {
        if (cursor is not { Length: > 0 } && _descriptors.Length <= _pageSize)
        {
            return Fin.Succ<ResourcesListResult>(_allResourcesResult);
        }

        var start = 0;
        if (cursor is { Length: > 0 } cursorValue &&
            !McpOpaqueCursor.TryReadResourcesListCursor(cursorValue, _descriptors.Length, out start))
        {
            return Fin.Fail<ResourcesListResult>(Error.New("Invalid resources/list cursor."));
        }

        if (start == _descriptors.Length)
        {
            return Fin.Succ<ResourcesListResult>(new ResourcesListResult());
        }

        var count = Math.Min(_pageSize, _descriptors.Length - start);
        var page = new McpResourceDescriptor[count];
        System.Array.Copy(_descriptors, start, page, 0, count);

        var next = start + count;
        var result = new ResourcesListResult
        {
            Resources = page,
            NextCursor = next < _descriptors.Length ? McpOpaqueCursor.CreateResourcesListCursor(next, _descriptors.Length) : default
        };

        return Fin.Succ<ResourcesListResult>(result);
    }

    public Fin<ResourceTemplatesListResult> ListResourceTemplates()
    {
        return Fin.Succ<ResourceTemplatesListResult>(_emptyTemplatesResult);
    }

    public Fin<IMcpResource> FindResource(string uri)
    {
        if (!McpResourceUriValidator.IsValid(uri))
        {
            return Fin.Fail<IMcpResource>(Error.New("Resource uri is invalid."));
        }

        return _resourcesByUri.TryGetValue(uri, out var resource)
            ? Fin.Succ<IMcpResource>(resource)
            : Fin.Fail<IMcpResource>(Error.New($"Resource '{uri}' was not found."));
    }

    private static void ValidateDescriptor(McpResourceDescriptor descriptor, System.Collections.Generic.HashSet<string> uris)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!McpResourceUriValidator.IsValid(descriptor.Uri))
        {
            throw new InvalidOperationException($"Resource descriptor uri '{descriptor.Uri}' is invalid.");
        }

        if (!uris.Add(descriptor.Uri))
        {
            throw new InvalidOperationException($"Duplicate MCP resource uri '{descriptor.Uri}' was registered.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            throw new InvalidOperationException($"Resource '{descriptor.Uri}' must provide a name.");
        }

        if (descriptor.Size is < 0)
        {
            throw new InvalidOperationException($"Resource '{descriptor.Uri}' size must not be negative.");
        }

        if (descriptor.Icons is { Length: > 0 } icons)
        {
            for (var i = 0; i < icons.Length; i++)
            {
                var icon = icons[i];
                if (icon is not { Src: { } source } || !McpIconValidator.IsValidSource(source))
                {
                    throw new InvalidOperationException($"Resource '{descriptor.Uri}' icon at index {i.ToString(CultureInfo.InvariantCulture)} must provide an HTTP, HTTPS, or data URI src.");
                }

                if (!McpIconValidator.IsValidMimeType(icon.MimeType))
                {
                    throw new InvalidOperationException($"Resource '{descriptor.Uri}' icon at index {i.ToString(CultureInfo.InvariantCulture)} has an unsupported MIME type.");
                }

                if (!McpIconValidator.IsValidTheme(icon.Theme))
                {
                    throw new InvalidOperationException($"Resource '{descriptor.Uri}' icon at index {i.ToString(CultureInfo.InvariantCulture)} has an invalid theme.");
                }
            }
        }
    }
}
