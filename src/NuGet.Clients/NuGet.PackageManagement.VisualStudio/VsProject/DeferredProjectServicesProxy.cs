// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft;
using Microsoft.VisualStudio.ComponentModelHost;
using NuGet.ProjectManagement;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    internal class DeferredProjectServicesProxy : INuGetProjectServices
    {
        private readonly IVsProjectAdapter _vsProjectAdapter;
        private readonly IComponentModel _componentModel;
        private readonly WorkspaceProjectServices _deferredProjectServices;
        private readonly Lazy<INuGetProjectServices> _fallbackProjectServices;
        private readonly IVsProjectThreadingService _threadingService;

        private bool IsDeferred
        {
            get
            {
               return _threadingService.JoinableTaskFactory.Run(async delegate
               {
                   await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                   return _vsProjectAdapter.IsDeferred;
               });
            }
        }

        public DeferredProjectServicesProxy(
            IVsProjectAdapter vsProjectAdapter,
            Func<INuGetProjectServices> getFallbackProjectServices,
            IComponentModel componentModel,
            IVsProjectThreadingService threadingService)
        {
            Assumes.Present(vsProjectAdapter);
            Assumes.Present(getFallbackProjectServices);
            Assumes.Present(componentModel);

            _vsProjectAdapter = vsProjectAdapter;
            _componentModel = componentModel;
            _fallbackProjectServices = new Lazy<INuGetProjectServices>(getFallbackProjectServices);
            _threadingService = threadingService;
            _deferredProjectServices = new WorkspaceProjectServices(vsProjectAdapter, this);
        }

        public IProjectBuildProperties BuildProperties => _vsProjectAdapter.BuildProperties;

        public IProjectSystemCapabilities Capabilities
        {
            get
            {
                if (IsDeferred)
                {
                    return _deferredProjectServices;
                }

                return _fallbackProjectServices.Value.Capabilities;
            }
        }

        public IProjectSystemReferencesReader ItemsReader
        {
            get
            {
                if (IsDeferred)
                {
                    return _deferredProjectServices;
                }

                return _fallbackProjectServices.Value.ItemsReader;
            }
        }

        public IProjectSystemService ProjectSystem => _fallbackProjectServices.Value.ProjectSystem;

        public IProjectSystemReferencesService References => _fallbackProjectServices.Value.References;

        public T GetService<T>() where T : class => _componentModel.GetService<T>();
    }
}
