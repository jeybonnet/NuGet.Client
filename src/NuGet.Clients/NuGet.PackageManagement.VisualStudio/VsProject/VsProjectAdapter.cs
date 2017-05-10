// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace.Extensions.MSBuild;
using NuGet.Commands;
using NuGet.Frameworks;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.RuntimeModel;
using NuGet.VisualStudio;
using VSLangProj;

namespace NuGet.PackageManagement.VisualStudio
{
    [DebuggerDisplay("{ProjectName}")]
    internal class VsProjectAdapter : IVsProjectAdapter
    {
        #region Private members

        private readonly VsHierarchyItem _vsHierarchyItem;
        private EnvDTE.Project _dteProject;
        private readonly Func<IVsHierarchy, EnvDTE.Project> _loadDteProject;
        private readonly IProjectSystemCache _projectSystemCache;
        private readonly IDeferredProjectWorkspaceService _deferredProjectWorkspaceService;
        private readonly AsyncLazy<IMSBuildProjectDataService> _buildProjectDataService;

        #endregion Private members

        #region Properties

        public string BaseIntermediateOutputPath
        {
            get
            {
                var baseIntermediateOutputPath = GetBuildProperty("BaseIntermediateOutputPath");

                if (string.IsNullOrEmpty(baseIntermediateOutputPath))
                {
                    throw new InvalidOperationException(string.Format(
                        Strings.BaseIntermediateOutputPathNotFound,
                        FullPath));
                }

                var projectDirectory = Path.GetDirectoryName(FullPath);

                return Path.Combine(projectDirectory, baseIntermediateOutputPath);
            }
        }

        public string CustomUniqueName => ProjectNames.CustomUniqueName;

        public string FullName => ProjectNames.FullName;

        public string FullPath
        {
            get
            {
                if (!IsLoadDeferred)
                {
                    return EnvDTEProjectInfoUtility.GetFullPath(_dteProject);
                }
                else
                {
                    return Path.GetDirectoryName(FullProjectPath);
                }
            }
        }

        public string FullProjectPath { get; private set; }

        public bool IsLoadDeferred => _dteProject == null;

        public bool IsSupported
        {
            get
            {
                if (!IsLoadDeferred)
                {
                    return EnvDTEProjectUtility.IsSupported(_dteProject);
                }

                return true;
            }
        }

        public string PackageTargetFallback
        {
            get
            {
                return GetBuildProperty("PackageTargetFallback");
            }
        }

        public EnvDTE.Project Project
        {
            get
            {
                if (_dteProject == null)
                {
                    _dteProject = _loadDteProject(_vsHierarchyItem.VsHierarchy);
                }

                return _dteProject;
            }
        }

        public string ProjectId
        {
            get
            {
                Guid id;
                if (!_vsHierarchyItem.TryGetProjectId(out id))
                {
                    id = Guid.Empty;
                }

                return id.ToString();
            }
        }

        public string ProjectName => ProjectNames.ShortName;

        public ProjectNames ProjectNames { get; private set; }

        public string[] ProjectTypeGuids
        {
            get
            {
                if (!IsLoadDeferred)
                {
                    return VsHierarchyUtility.GetProjectTypeGuids(_dteProject);
                }
                else
                {
                    return VsHierarchyUtility.GetProjectTypeGuids(VsHierarchy);
                }
            }
        }

        public References References
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                dynamic projectObj = Project.Object;
                var references = (References)projectObj.References;
                projectObj = null;
                return references;
            }
        }

        public IEnumerable<CompatibilityProfile> Supports
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var unparsedRuntimeSupports = GetBuildProperty("RuntimeSupports");

                if (unparsedRuntimeSupports == null)
                {
                    return Enumerable.Empty<CompatibilityProfile>();
                }

                return unparsedRuntimeSupports
                    .Split(';')
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Select(support => new CompatibilityProfile(support));
            }
        }

        public string UniqueName => ProjectNames.UniqueName;

        public string Version
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var packageVersion = GetBuildProperty("PackageVersion");

                if (string.IsNullOrEmpty(packageVersion))
                {
                    packageVersion = GetBuildProperty("Version");

                    if (string.IsNullOrEmpty(packageVersion))
                    {
                        packageVersion = "1.0.0";
                    }
                }

                return packageVersion;
            }
        }

        public IVsHierarchy VsHierarchy => _vsHierarchyItem.VsHierarchy;

        #endregion Properties

        #region Constructors

        public VsProjectAdapter(
            EnvDTE.Project dteProject,
            IProjectSystemCache projectSystemCache)
        {
            Assumes.Present(dteProject);
            Assumes.Present(projectSystemCache);

            _vsHierarchyItem = VsHierarchyItem.FromDteProject(dteProject);
            _dteProject = dteProject;
            _projectSystemCache = projectSystemCache;

            FullProjectPath = EnvDTEProjectInfoUtility.GetFullProjectPath(_dteProject);
            ProjectNames = ProjectNames.FromDTEProject(_dteProject);
        }

        public VsProjectAdapter(
            IVsHierarchy project,
            ProjectNames projectNames,
            Func<IVsHierarchy, EnvDTE.Project> loadDteProject,
            IProjectSystemCache projectSystemCache,
            IDeferredProjectWorkspaceService deferredProjectWorkspaceService)
        {
            Assumes.Present(project);
            Assumes.Present(projectNames);
            Assumes.Present(loadDteProject);
            Assumes.Present(projectSystemCache);
            Assumes.Present(deferredProjectWorkspaceService);

            _vsHierarchyItem = VsHierarchyItem.FromVsHierarchy(project);
            _loadDteProject = loadDteProject;
            _projectSystemCache = projectSystemCache;
            _deferredProjectWorkspaceService = deferredProjectWorkspaceService;

            FullProjectPath = VsHierarchyUtility.GetProjectPath(project);
            ProjectNames = projectNames;

            _buildProjectDataService = new AsyncLazy<IMSBuildProjectDataService>(
                () => _deferredProjectWorkspaceService.GetMSBuildProjectDataServiceAsync(FullProjectPath),
                NuGetUIThreadHelper.JoinableTaskFactory);
        }

        #endregion Constructors

        #region Getters

        public string GetBuildProperty(string propertyName)
        {
            return VsHierarchyUtility.GetBuildProperty(AsVsBuildPropertyStorage, propertyName);
        }

        public IEnumerable<string> GetChildItems(string path, string filter, string desiredKind)
        {
            return NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var childItems = await EnvDTEProjectUtility.GetChildItems(Project, path, filter, VsProjectTypes.VsProjectItemKindPhysicalFile);
                // Get all physical files
                return from p in childItems
                       select p.Name;
            });
        }

        public async Task<IReadOnlyList<ProjectRestoreReference>> GetDirectProjectReferencesAsync(IEnumerable<string> resolvedProjects, Common.ILogger logger)
        {
            if (_deferredProjectWorkspaceService != null)
            {
                var references = await _deferredProjectWorkspaceService.GetProjectReferencesAsync(FullProjectPath);

                return references
                    .Select(reference => new ProjectRestoreReference
                    {
                        ProjectPath = reference,
                        ProjectUniqueName = reference
                    })
                    .ToList();
            }
            else
            {
                return await VSProjectRestoreReferenceUtility.GetDirectProjectReferencesAsync(Project, resolvedProjects, logger);
            }
        }

        public IEnumerable<string> GetFullPaths(string fileName)
        {
            return NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var paths = new List<string>();
                var projectItemsQueue = new Queue<EnvDTE.ProjectItems>();
                projectItemsQueue.Enqueue(Project.ProjectItems);
                while (projectItemsQueue.Count > 0)
                {
                    var items = projectItemsQueue.Dequeue();
                    foreach (var item in items.Cast<EnvDTE.ProjectItem>())
                    {
                        if (item.Kind == VsProjectTypes.VsProjectItemKindPhysicalFile)
                        {
                            if (StringComparer.OrdinalIgnoreCase.Equals(item.Name, fileName))
                            {
                                paths.Add(item.FileNames[1]);
                            }
                        }
                        else if (item.Kind == VsProjectTypes.VsProjectItemKindPhysicalFolder)
                        {
                            projectItemsQueue.Enqueue(item.ProjectItems);
                        }
                    }
                }

                return paths;
            });
        }

        public async Task<EnvDTE.ProjectItems> GetProjectItemsAsync(string folderPath, bool createIfNotExists)
        {
            return await EnvDTEProjectUtility.GetProjectItemsAsync(Project, folderPath, createIfNotExists);
        }

        public async Task<EnvDTE.ProjectItem> GetProjectItemAsync(string path)
        {
            return await EnvDTEProjectUtility.GetProjectItemAsync(Project, path);
        }

        public dynamic GetProjectProperty(string propertyName)
        {
            try
            {
                var envDTEProperty = Project.Properties.Item(propertyName);
                if (envDTEProperty != null)
                {
                    return envDTEProperty.Value;
                }
            }
            catch (ArgumentException)
            {
                // If the property doesn't exist this will throw an argument exception
            }
            return null;
        }

        public IList<IVsProjectAdapter> GetReferencedProjects()
        {
            if (!IsLoadDeferred)
            {
                var referencedProjects = new List<IVsProjectAdapter>();
                var dteProjects = EnvDTEProjectUtility.GetReferencedProjects(Project);
                foreach(var dteProject in dteProjects)
                {
                    var result = _projectSystemCache.TryGetVsProjectAdapter(dteProject.UniqueName, out IVsProjectAdapter projectAdapter);
                    
                    if (result)
                    {
                        referencedProjects.Add(projectAdapter);
                    }
                }

                return referencedProjects;
            }
            else
            {
                return NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    var projectsPath = await _deferredProjectWorkspaceService.GetProjectReferencesAsync(FullProjectPath);
                    var referencedProjects = new List<IVsProjectAdapter>();

                    foreach (var projectPath in projectsPath)
                    {
                        var result = _projectSystemCache.TryGetVsProjectAdapter(projectPath, out IVsProjectAdapter projectAdapter);

                        if (result)
                        {
                            referencedProjects.Add(projectAdapter);
                        }
                    }

                    return referencedProjects;
                });
            }
        }

        public IEnumerable<RuntimeDescription> GetRuntimes()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var unparsedRuntimeIdentifer = GetBuildProperty("RuntimeIdentifier");
            var unparsedRuntimeIdentifers = GetBuildProperty("RuntimeIdentifiers");

            var runtimes = Enumerable.Empty<string>();

            if (unparsedRuntimeIdentifer != null)
            {
                runtimes = runtimes.Concat(new[] { unparsedRuntimeIdentifer });
            }

            if (unparsedRuntimeIdentifers != null)
            {
                runtimes = runtimes.Concat(unparsedRuntimeIdentifers.Split(';'));
            }

            runtimes = runtimes
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x));

            return runtimes
                .Select(runtime => new RuntimeDescription(runtime));
        }

        public NuGetFramework GetTargetFramework()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var nugetFramework = NuGetFramework.UnsupportedFramework;
            var projectPath = FullPath;
            var platformIdentifier = GetBuildProperty("TargetPlatformIdentifier");
            var platformVersion = GetBuildProperty("TargetPlatformVersion");
            var platformMinVersion = GetBuildProperty("TargetPlatformMinVersion");
            var targetFrameworkMoniker = GetBuildProperty("TargetFrameworkMoniker");

            // Projects supporting TargetFramework and TargetFrameworks are detected before
            // this check. The values can be passed as null here.
            var frameworkStrings = MSBuildProjectFrameworkUtility.GetProjectFrameworkStrings(
                projectFilePath: projectPath,
                targetFrameworks: null,
                targetFramework: null,
                targetFrameworkMoniker: targetFrameworkMoniker,
                targetPlatformIdentifier: platformIdentifier,
                targetPlatformVersion: platformVersion,
                targetPlatformMinVersion: platformMinVersion,
                isManagementPackProject: false,
                isXnaWindowsPhoneProject: false);

            var frameworkString = frameworkStrings.FirstOrDefault();

            if (!string.IsNullOrEmpty(frameworkString))
            {
                nugetFramework = NuGetFramework.Parse(frameworkString);
            }

            return nugetFramework;
        }

        public async Task<NuGetFramework> GetTargetFrameworkAsync()
        {
            if (!IsLoadDeferred)
            {
                return EnvDTEProjectInfoUtility.GetTargetNuGetFramework(Project);
            }
            else
            {
                var msbuildProjectDataService = await _buildProjectDataService.GetValueAsync();

                var nuGetFramework = await SolutionWorkspaceUtility.GetNuGetFrameworkAsync(msbuildProjectDataService, FullProjectPath);

                return nuGetFramework;
            }
        }

        #endregion Getters

        #region Capabilities

        public async Task<bool> ContainsFile(string path)
        {
            return await EnvDTEProjectUtility.ContainsFile(Project, path);
        }

        public bool SupportsBindingRedirects
        {
            get
            {
                return EnvDTEProjectUtility.SupportsBindingRedirects(Project);
            }
        }

        public bool SupportsProjectSystemService
        {
            get
            {
                return !IsLoadDeferred && EnvDTEProjectUtility.SupportsProjectSystemService(Project);
            }
        }

        public bool SupportsReference
        {
            get
            {
                if (!IsLoadDeferred)
                {
                    return EnvDTEProjectUtility.SupportsReferences(Project);
                }
                else
                {
                    return !ProjectTypeGuids.Any(p => ProjectTypesConstant.UnsupportedProjectTypesForAddingReferences.Contains(p));
                }
            }
        }

        #endregion Capablities

        #region Public methods

        public void AddImportStatement(string targetsPath, ImportLocation location)
        {
            EnvDTEProjectUtility.AddImportStatement(Project, targetsPath, location);
        }

        public async Task<bool> DeleteProjectItemAsync(string path)
        {
            return await EnvDTEProjectUtility.DeleteProjectItemAsync(Project, path);
        }

        public void EnsureCheckedOutIfExists(string root, string path)
        {
            EnvDTEProjectUtility.EnsureCheckedOutIfExists(Project, root, path);
        }

        public void RemoveImportStatement(string targetsPath)
        {
            EnvDTEProjectUtility.RemoveImportStatement(Project, targetsPath);
        }

        public void Save()
        {
            EnvDTEProjectUtility.Save(Project);
        }

        #endregion Public methods

        #region Private methods

        private IVsBuildPropertyStorage AsVsBuildPropertyStorage
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var bps = _vsHierarchyItem.VsHierarchy as IVsBuildPropertyStorage;

                Assumes.True(
                    bps != null, 
                    string.Format(Strings.ProjectCouldNotBeCastedToBuildPropertyStorage, FullPath));

                return bps;
            }
        }

        #endregion Private methods
    }
}
