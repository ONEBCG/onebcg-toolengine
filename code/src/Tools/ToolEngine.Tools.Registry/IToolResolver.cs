namespace ToolEngine.Tools.Registry;

using ToolEngine.Core.Domain.Common;
using ToolEngine.Tools.Abstractions.Interfaces;

/// <summary>
/// Resolves an IToolHandler instance from the DI container given a descriptor.
/// Implemented in the DI layer (not here) to avoid taking a container dependency.
/// </summary>
public interface IToolResolver
{
    Result<IToolHandler<TInput, TOutput>> Resolve<TInput, TOutput>(
        ToolDescriptor descriptor);
}
