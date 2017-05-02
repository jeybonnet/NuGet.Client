// Copyright (c) .NET Foundation. All rights reserved.
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
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    [Export(typeof(IVsProjectAdapterProvider))]
    internal class VsProjectAdapterProvider : IVsProjectAdapterProvider
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDeferredProjectWorkspaceService _deferredProjectWorkspaceService;
        private readonly IProjectSystemCache _projectSystemCache;

        private readonly Lazy<IVsSolution> _vsSolution;

        [ImportingConstructor]
        public VsProjectAdapterProvider(
            [Import(typeof(SVsServiceProvider))]
            IServiceProvider serviceProvider,
            IDeferredProjectWorkspaceService deferredProjectWorkspaceService,
            IProjectSystemCache projectSystemCache)
        {
            Assumes.Present(serviceProvider);
            Assumes.Present(deferredProjectWorkspaceService);

            _serviceProvider = serviceProvider;
            _deferredProjectWorkspaceService = deferredProjectWorkspaceService;
            _projectSystemCache = projectSystemCache;
            _vsSolution = new Lazy<IVsSolution>(() => _serviceProvider.GetService<SVsSolution, IVsSolution>());
        }

        public IVsProjectAdapter CreateVsProject(EnvDTE.Project dteProject)
        {
            Assumes.Present(dteProject);

            return new VsProjectAdapter(dteProject, _projectSystemCache);
        }

        public async Task<IVsProjectAdapter> CreateVsProjectAsync(IVsHierarchy project)
        {
            Assumes.Present(project);

            await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var projectPath = VsHierarchyUtility.GetProjectPath(project);

            var uniqueName = string.Empty;
            _vsSolution.Value.GetUniqueNameOfProject(project, out uniqueName);

            var projectNames = new ProjectNames(
                fullName: projectPath,
                uniqueName: uniqueName,
                shortName: Path.GetFileNameWithoutExtension(projectPath),
                customUniqueName: uniqueName);

            return new VsProjectAdapter(project, projectNames, EnsureProjectIsLoaded, _projectSystemCache, _deferredProjectWorkspaceService);
        }

        public EnvDTE.Project EnsureProjectIsLoaded(IVsHierarchy project)
        {
            return NuGetUIThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // 1. Ask the solution to load the required project. To reduce wait time,
                //    we load only the project we need, not the entire solution.
                var hr = project.GetGuidProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ProjectIDGuid, out Guid projectGuid);
                ErrorHandler.ThrowOnFailure(hr);

                var asVsSolution4 = _vsSolution.Value as IVsSolution4;
                Assumes.Present(asVsSolution4);
                hr = asVsSolution4.EnsureProjectIsLoaded(projectGuid, (uint)__VSBSLFLAGS.VSBSLFLAGS_None);
                ErrorHandler.ThrowOnFailure(hr);

                // 2. After the project is loaded, grab the latest IVsHierarchy object.
                hr = _vsSolution.Value.GetProjectOfGuid(projectGuid, out IVsHierarchy loadedProject);
                ErrorHandler.ThrowOnFailure(hr);

                Assumes.Present(loadedProject);

                object extObject = null;
                hr = loadedProject.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_ExtObject, out extObject);
                ErrorHandler.ThrowOnFailure(hr);

                var dteProject = extObject as EnvDTE.Project;

                return dteProject;
            });
        }
    }
}
