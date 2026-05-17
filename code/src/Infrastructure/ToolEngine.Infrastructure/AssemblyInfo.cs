using System.Runtime.CompilerServices;

// Allow integration tests to reference internal infrastructure types directly
// (Repository, ReadRepository, UnitOfWork, CachedTenantReadRepository,
//  SystemDateTimeProvider, MemoryCacheProvider) without promoting them to public.
[assembly: InternalsVisibleTo("ToolEngine.Integration.Tests")]
