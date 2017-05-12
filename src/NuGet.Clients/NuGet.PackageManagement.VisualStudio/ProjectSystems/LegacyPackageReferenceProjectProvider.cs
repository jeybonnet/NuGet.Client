﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using NuGet.Configuration;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.VisualStudio;
using VSLangProj150;
using ProjectSystem = Microsoft.VisualStudio.ProjectSystem;

namespace NuGet.PackageManagement.VisualStudio
{
    [Export, Export(typeof(INuGetProjectProvider))]
    [Name(nameof(LegacyPackageReferenceProjectProvider))]
    [Order(After = nameof(CpsPackageReferenceProjectProvider))]
    public sealed class LegacyPackageReferenceProjectProvider : INuGetProjectProvider
    {
        private static readonly string PackageReference = ProjectStyle.PackageReference.ToString();

        private readonly IVsProjectThreadingService _threadingService;
        private readonly AsyncLazy<IComponentModel> _componentModel;

        // Reason it's lazy<object> is because we don't want to load any CPS assemblies untill
        // we're really going to use any of CPS api. Which is why we also don't use nameof or typeof apis.
        [Import("Microsoft.VisualStudio.ProjectSystem.IProjectServiceAccessor")]
        private Lazy<object> ProjectServiceAccessor { get; set; }

        [ImportingConstructor]
        public LegacyPackageReferenceProjectProvider(
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

        public bool TryCreateNuGetProject(
            IVsProjectAdapter vsProjectAdapter,
            ProjectSystemProviderContext context,
            out NuGetProject result)
        {
            Assumes.Present(vsProjectAdapter);
            Assumes.Present(context);

            _threadingService.VerifyOnUIThread();

            result = null;

            var projectServices = _threadingService.ExecuteSynchronously(
                () => TryCreateProjectServices(vsProjectAdapter));

            if (projectServices == null)
            {
                return false;
            }

            // Lazy load the CPS enabled JoinableTaskFactory for the UI.
            NuGetUIThreadHelper.SetJoinableTaskFactoryFromService(ProjectServiceAccessor.Value as ProjectSystem.IProjectServiceAccessor);

            result = new LegacyPackageReferenceProject(
                vsProjectAdapter,
                vsProjectAdapter.ProjectId,
                projectServices,
                _threadingService);

            return true;
        }

        /// <summary>
        /// Is this project a non-CPS package reference based csproj?
        /// </summary>
        private async Task<INuGetProjectServices> TryCreateProjectServices(IVsProjectAdapter vsProjectAdapter)
        {
            var componentModel = await _componentModel.GetValueAsync();

            // Check for RestoreProjectStyle property
            var restoreProjectStyle = await vsProjectAdapter.BuildProperties.GetPropertyValueAsync(
                ProjectBuildProperties.RestoreProjectStyle);

            if (vsProjectAdapter.IsDeferred)
            {
                if (!PackageReference.Equals(restoreProjectStyle, StringComparison.OrdinalIgnoreCase))
                {
                    var packagesConfigFilePath = Path.Combine(vsProjectAdapter.FullPath, NuGetConstants.PackageReferenceFile);
                    if (await vsProjectAdapter.EntityExists(packagesConfigFilePath))
                    {
                        return null;
                    }

                    var projectJsonFilePath = Path.Combine(vsProjectAdapter.FullPath, NuGetConstants.PackageSpecFileName);
                    if (await vsProjectAdapter.EntityExists(projectJsonFilePath))
                    {
                        return null;
                    }
                }

                var projectServices = componentModel.GetService<VsProjectSystemServices>();
                Assumes.Present(projectServices);

                return new DeferredProjectServicesProxy(
                    vsProjectAdapter,
                    () => CreateCoreProjectSystemServices(vsProjectAdapter, componentModel),
                    componentModel);
            }
            else
            {
                var asVSProject4 = vsProjectAdapter.Project.Object as VSProject4;

                // A legacy CSProj must cast to VSProject4 to manipulate package references
                if (asVSProject4 == null)
                {
                    return null;
                }

                // For legacy csproj, either the RestoreProjectStyle must be set to PackageReference or
                // project has atleast one package dependency defined as PackageReference
                if (PackageReference.Equals(restoreProjectStyle, StringComparison.OrdinalIgnoreCase)
                    || (asVSProject4.PackageReferences?.InstalledPackages?.Length ?? 0) > 0)
                {
                    return CreateCoreProjectSystemServices(vsProjectAdapter, componentModel);
                }
            }

            return null;
        }

        private static INuGetProjectServices CreateCoreProjectSystemServices(IVsProjectAdapter vsProjectAdapter, IComponentModel componentModel)
        {
            var vsProjectServices = componentModel.GetService<VsProjectSystemServices>();
            Assumes.Present(vsProjectServices);

            var vsLangProjectSystemService = new VsLangProjectSystemService(vsProjectAdapter, vsProjectServices);

            vsProjectServices.ProjectAdapter = vsProjectAdapter;
            vsProjectServices.Capabilities = vsLangProjectSystemService;
            vsProjectServices.ItemsReader = vsLangProjectSystemService;
            vsProjectServices.References = vsLangProjectSystemService;

            return vsProjectServices;
        }
    }
}
