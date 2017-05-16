// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;

namespace NuGet.ProjectManagement
{
    public interface IProjectSystemService
    {
        /// <summary>
        /// Saves the underlying project.
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Completion task</returns>
        Task SaveAsync(CancellationToken token);
    }

    public interface IProjectSystemCapabilities
    {
        bool SupportsPackageReferences { get; }
    }

    public interface IProjectBuildProperties
    {
        string GetPropertyValue(string propertyName);
        Task<string> GetPropertyValueAsync(string propertyName);
    }

    public static class ProjectBuildProperties
    {
        public const string BaseIntermediateOutputPath = "BaseIntermediateOutputPath";
        public const string PackageTargetFallback = "PackageTargetFallback";
        public const string PackageVersion = "PackageVersion";
        public const string RestoreProjectStyle = "RestoreProjectStyle";
        public const string RuntimeIdentifier = "RuntimeIdentifier";
        public const string RuntimeIdentifiers = "RuntimeIdentifiers";
        public const string RuntimeSupports = "RuntimeSupports";
        public const string TargetFramework = "TargetFramework";
        public const string TargetFrameworkMoniker = "TargetFrameworkMoniker";
        public const string TargetFrameworks = "TargetFrameworks";
        public const string TargetPlatformIdentifier = "TargetPlatformIdentifier";
        public const string TargetPlatformMinVersion = "TargetPlatformMinVersion";
        public const string TargetPlatformVersion = "TargetPlatformVersion";
        public const string Version = "Version";
    }

    public static class ProjectItemProperties
    {
        public const string IncludeAssets = "IncludeAssets";
        public const string ExcludeAssets = "ExcludeAssets";
        public const string PrivateAssets = "PrivateAssets";
    }

    public static class ProjectItems
    {
        public const string PackageReference = "PackageReference";
        public const string ProjectReference = "ProjectReference";
    }

    public interface IProjectSystemReferencesReader
    {
        Task<IReadOnlyList<LibraryDependency>> GetPackageReferencesAsync(
            NuGetFramework targetFramework);

        Task<IEnumerable<ProjectRestoreReference>> GetProjectReferencesAsync(
            Common.ILogger logger);
    }

    public interface IProjectSystemReferencesService
    {
        /// <summary>
        /// Add a new package reference or update an existing one
        /// </summary>
        Task AddOrUpdatePackageReferenceAsync(LibraryDependency packageReference);

        /// <summary>
        /// Remove a package reference from a legacy CSProj project
        /// </summary>
        /// <param name="packageName">Name of package to remove from project</param>
        void RemovePackageReference(string packageName);
    }

    /// <summary>
    /// Provides access to common <see cref="NuGetProject"/> scoped services, such as
    /// - project references
    /// - assembly references
    /// - project capabilities
    /// - binding redirects
    /// - script executor
    /// </summary>
    public interface INuGetProjectServices
    {
        IProjectBuildProperties BuildProperties { get; }
        IProjectSystemCapabilities Capabilities { get; }
        IProjectSystemReferencesReader ReferencesReader { get; }
        IProjectSystemReferencesService References { get; }
        IProjectSystemService ProjectSystem { get; }

        // generic catalog of services
        T GetService<T>() where T : class;
    }

    internal sealed class DefaultProjectServices
        : INuGetProjectServices
        , IProjectBuildProperties
        , IProjectSystemCapabilities
        , IProjectSystemReferencesReader
        , IProjectSystemReferencesService
        , IProjectSystemService
    {
        public static INuGetProjectServices Instance { get; } = new DefaultProjectServices();

        public IProjectBuildProperties BuildProperties => this;
        public IProjectSystemCapabilities Capabilities => this;
        public IProjectSystemReferencesReader ReferencesReader => this;
        public IProjectSystemService ProjectSystem => this;
        public IProjectSystemReferencesService References => this;

        public bool SupportsPackageReferences => false;

        public Task AddOrUpdatePackageReferenceAsync(LibraryDependency packageReference)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<LibraryDependency>> GetPackageReferencesAsync(
            NuGetFramework targetFramework)
        {
            throw new NotSupportedException();
        }

        public Task<IEnumerable<ProjectRestoreReference>> GetProjectReferencesAsync(Common.ILogger logger)
        {
            return Task.FromResult(Enumerable.Empty<ProjectRestoreReference>());
        }

        public string GetPropertyValue(string propertyName)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetPropertyValueAsync(string propertyName)
        {
            throw new NotImplementedException();
        }

        public T GetService<T>() where T : class
        {
            throw new NotImplementedException();
        }

        public void RemovePackageReference(string packageName)
        {
            throw new NotImplementedException();
        }

        public Task SaveAsync(CancellationToken _)
        {
            // do nothing
            return Task.FromResult(0);
        }
    }
}
