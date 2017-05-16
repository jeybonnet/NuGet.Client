// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using Microsoft;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using NuGet.ProjectManagement;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    [Export(typeof(INuGetProjectProvider))]
    [Name(nameof(MSBuildNuGetProjectProvider))]
    [Order(After = nameof(ProjectJsonProjectProvider))]
    internal class MSBuildNuGetProjectProvider : INuGetProjectProvider
    {
        private readonly IVsProjectThreadingService _threadingService;
        private readonly Lazy<IComponentModel> _componentModel;

        [ImportingConstructor]
        public MSBuildNuGetProjectProvider(
            [Import(typeof(SVsServiceProvider))]
            IServiceProvider vsServiceProvider,
            IVsProjectThreadingService threadingService)
        {
            Assumes.Present(vsServiceProvider);
            Assumes.Present(threadingService);

            _threadingService = threadingService;

            _componentModel = new Lazy<IComponentModel>(
                () => vsServiceProvider.GetService<SComponentModel, IComponentModel>());
        }

        public bool TryCreateNuGetProject(IVsProjectAdapter vsProjectAdapter, ProjectSystemProviderContext context, out NuGetProject result)
        {
            Assumes.Present(vsProjectAdapter);
            Assumes.Present(context);

            _threadingService.VerifyOnUIThread();

            result = null;

            var projectSystem = MSBuildNuGetProjectSystemFactory.CreateMSBuildNuGetProjectSystem(
                vsProjectAdapter,
                context.ProjectContext);

            var projectServices = CreateProjectServices(vsProjectAdapter, projectSystem as VsMSBuildProjectSystem);

            var folderNuGetProjectFullPath = context.PackagesPathFactory();

            // Project folder path is the packages config folder path
            var packagesConfigFolderPath = vsProjectAdapter.FullPath;

            result = new VsMSBuildNuGetProject(
                vsProjectAdapter,
                projectSystem,
                folderNuGetProjectFullPath,
                packagesConfigFolderPath,
                projectServices);

            return result != null;
        }

        private INuGetProjectServices CreateProjectServices(
            IVsProjectAdapter vsProjectAdapter, VsMSBuildProjectSystem projectSystem)
        {
            var componentModel = _componentModel.Value;

            if (vsProjectAdapter.IsDeferred)
            {
                return new DeferredProjectServicesProxy(
                    vsProjectAdapter,
                    () => CreateCoreProjectSystemServices(
                        vsProjectAdapter, projectSystem, componentModel),
                    componentModel,
                    _threadingService);
            }
            else
            {
                return CreateCoreProjectSystemServices(vsProjectAdapter, projectSystem, componentModel);
            }
        }

        private static INuGetProjectServices CreateCoreProjectSystemServices(IVsProjectAdapter vsProjectAdapter, VsMSBuildProjectSystem projectSystem, IComponentModel componentModel)
        {
            var projectServices = componentModel.GetService<VsProjectSystemServices>();
            Assumes.Present(projectServices);

            projectServices.ProjectAdapter = vsProjectAdapter;
            projectServices.Capabilities = projectSystem;
            projectServices.ReferencesReader = projectSystem;
            projectServices.ProjectSystem = projectSystem;
            projectServices.References = projectSystem;

            return projectServices;
        }
    }

    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    internal class VsProjectSystemServices : INuGetProjectServices
    {
        private readonly IVsProjectThreadingService _threadingService;
        private readonly AsyncLazy<IComponentModel> _componentModel;

        public IVsProjectAdapter ProjectAdapter { get; set; }

        public IProjectBuildProperties BuildProperties
        {
            get
            {
                Assumes.Present(ProjectAdapter);
                return ProjectAdapter.BuildProperties;
            }
        }

        public IProjectSystemCapabilities Capabilities { get; set; }

        public IProjectSystemReferencesReader ReferencesReader { get; set; }

        public IProjectSystemService ProjectSystem { get; set; }

        public IProjectSystemReferencesService References { get; set; }

        [ImportingConstructor]
        public VsProjectSystemServices(
            [Import(typeof(SVsServiceProvider))]
            IServiceProvider vsServiceProvider,
            IVsProjectThreadingService threadingService)
        {
            Assumes.Present(vsServiceProvider);
            Assumes.Present(threadingService);

            _threadingService = threadingService;

            _componentModel = new AsyncLazy<IComponentModel>(
                async () =>
                {
                    await _threadingService.JoinableTaskFactory.SwitchToMainThreadAsync();
                    return vsServiceProvider.GetService<SComponentModel, IComponentModel>();
                },
                _threadingService.JoinableTaskFactory);
        }

        public T GetService<T>() where T : class
        {
            return _threadingService.ExecuteSynchronously(
                async () => {
                    var componentModel = await _componentModel.GetValueAsync();
                    return componentModel.GetService<T>();
                });
        }
    }
}
