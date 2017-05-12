﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using NuGet.ProjectManagement;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    [Export(typeof(IVsProjectAdapterProvider))]
    internal class VsProjectAdapterProvider : IVsProjectAdapterProvider
    {
        private readonly IDeferredProjectWorkspaceService _workspaceService;
        private readonly IVsProjectThreadingService _threadingService;

        private readonly Lazy<IVsSolution> _vsSolution;

        [ImportingConstructor]
        public VsProjectAdapterProvider(
            [Import(typeof(SVsServiceProvider))]
            IServiceProvider serviceProvider,
            IDeferredProjectWorkspaceService workspaceService,
            IVsProjectThreadingService threadingService)
        {
            Assumes.Present(serviceProvider);
            Assumes.Present(workspaceService);
            Assumes.Present(threadingService);

            _workspaceService = workspaceService;
            _threadingService = threadingService;

            _vsSolution = new Lazy<IVsSolution>(() => serviceProvider.GetService<SVsSolution, IVsSolution>());
        }

        public IVsProjectAdapter CreateVsProject(EnvDTE.Project dteProject)
        {
            Assumes.Present(dteProject);

            _threadingService.VerifyOnUIThread();

            var vsHierarchyItem = VsHierarchyItem.FromDteProject(dteProject);
            Func<IVsHierarchy, EnvDTE.Project> loadDteProject = _ => dteProject;

            IProjectBuildProperties vsBuildProperties;
            if (vsHierarchyItem.VsHierarchy is IVsBuildPropertyStorage)
            {
                vsBuildProperties = new VsLangProjectBuildProperties(
                    vsHierarchyItem.VsHierarchy as IVsBuildPropertyStorage, _threadingService);
            }
            else
            {
                vsBuildProperties = new VsCoreProjectBuildProperties(dteProject, _threadingService);
            }

            var projectNames = ProjectNames.FromDTEProject(dteProject);
            var fullProjectPath = EnvDTEProjectInfoUtility.GetFullProjectPath(dteProject);
            return new VsProjectAdapter(
                vsHierarchyItem,
                projectNames,
                fullProjectPath,
                loadDteProject,
                vsBuildProperties,
                _threadingService);
        }

        public async Task<IVsProjectAdapter> CreateVsProjectAsync(IVsHierarchy project)
        {
            Assumes.Present(project);

            await _threadingService.JoinableTaskFactory.SwitchToMainThreadAsync();

            var vsHierarchyItem = VsHierarchyItem.FromVsHierarchy(project);
            var fullProjectPath = VsHierarchyUtility.GetProjectPath(project);

            var uniqueName = string.Empty;
            _vsSolution.Value.GetUniqueNameOfProject(project, out uniqueName);

            var projectNames = new ProjectNames(
                fullName: fullProjectPath,
                uniqueName: uniqueName,
                shortName: Path.GetFileNameWithoutExtension(fullProjectPath),
                customUniqueName: uniqueName);

            var workspaceBuildProperties = new WorkspaceProjectBuildProperties(
                fullProjectPath, _workspaceService, _threadingService);

            return new VsProjectAdapter(
                vsHierarchyItem,
                projectNames, 
                fullProjectPath,
                EnsureProjectIsLoaded,
                workspaceBuildProperties,
                _threadingService,
                _workspaceService);
        }

        public EnvDTE.Project EnsureProjectIsLoaded(IVsHierarchy project)
        {
            return _threadingService.ExecuteSynchronously(async () =>
            {
                await _threadingService.JoinableTaskFactory.SwitchToMainThreadAsync();

                // 1. Ask the solution to load the required project. To reduce wait time,
                //    we load only the project we need, not the entire solution.
                ErrorHandler.ThrowOnFailure(project.GetGuidProperty(
                    (uint)VSConstants.VSITEMID.Root, 
                    (int)__VSHPROPID.VSHPROPID_ProjectIDGuid, 
                    out Guid projectGuid));

                var asVsSolution4 = _vsSolution.Value as IVsSolution4;
                Assumes.Present(asVsSolution4);

                ErrorHandler.ThrowOnFailure(asVsSolution4.EnsureProjectIsLoaded(
                    projectGuid, 
                    (uint)__VSBSLFLAGS.VSBSLFLAGS_None));

                // 2. After the project is loaded, grab the latest IVsHierarchy object.
                ErrorHandler.ThrowOnFailure(_vsSolution.Value.GetProjectOfGuid(
                    projectGuid, 
                    out IVsHierarchy loadedProject));
                Assumes.Present(loadedProject);

                object extObject = null;
                ErrorHandler.ThrowOnFailure(loadedProject.GetProperty(
                    (uint)VSConstants.VSITEMID.Root, 
                    (int)__VSHPROPID.VSHPROPID_ExtObject, 
                    out extObject));

                var dteProject = extObject as EnvDTE.Project;
                Assumes.Present(dteProject);

                return dteProject;
            });
        }
    }
}
