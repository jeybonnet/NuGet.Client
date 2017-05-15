﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using NuGet.ProjectManagement;
using NuGet.VisualStudio;
using IMSBuildNuGetProjectSystemThunk = System.Func<NuGet.VisualStudio.IVsProjectAdapter, NuGet.ProjectManagement.INuGetProjectContext, NuGet.ProjectManagement.IMSBuildNuGetProjectSystem>;

namespace NuGet.PackageManagement.VisualStudio
{
    public static class MSBuildNuGetProjectSystemFactory
    {
        private static Dictionary<string, IMSBuildNuGetProjectSystemThunk> _factories = new Dictionary<string, IMSBuildNuGetProjectSystemThunk>(StringComparer.OrdinalIgnoreCase)
            {
                { VsProjectTypes.WebApplicationProjectTypeGuid, (project, nuGetProjectContext) => new WebProjectSystem(project, nuGetProjectContext) },
                { VsProjectTypes.WebSiteProjectTypeGuid, (project, nuGetProjectContext) => new WebSiteProjectSystem(project, nuGetProjectContext) },
                { VsProjectTypes.FsharpProjectTypeGuid, (project, nuGetProjectContext) => new FSharpProjectSystem(project, nuGetProjectContext) },
                { VsProjectTypes.WixProjectTypeGuid, (project, nuGetProjectContext) => new WixProjectSystem(project, nuGetProjectContext) },
                { VsProjectTypes.JsProjectTypeGuid, (project, nuGetProjectContext) => new JsProjectSystem(project, nuGetProjectContext) },
                { VsProjectTypes.WindowsStoreProjectTypeGuid, (project, nuGetProjectContext) => new WindowsStoreProjectSystem(project, nuGetProjectContext) },
                { VsProjectTypes.DeploymentProjectTypeGuid, (project, nuGetProjectContext) => new VsMSBuildProjectSystem(project, nuGetProjectContext) }
            };

        public static IMSBuildNuGetProjectSystem CreateMSBuildNuGetProjectSystem(IVsProjectAdapter vsProjectAdapter, INuGetProjectContext nuGetProjectContext)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (vsProjectAdapter == null)
            {
                throw new ArgumentNullException(nameof(vsProjectAdapter));
            }

            if (string.IsNullOrEmpty(vsProjectAdapter.FullName))
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.CurrentCulture,
                        Strings.DTE_ProjectUnsupported, vsProjectAdapter.ProjectName));
            }

            if (vsProjectAdapter.SupportsProjectSystemService)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.CurrentCulture, Strings.DTE_ProjectUnsupported, typeof(IMSBuildNuGetProjectSystem).FullName));
            }

            var guids = vsProjectAdapter.ProjectTypeGuids;
            if (guids.Contains(VsProjectTypes.CppProjectTypeGuid)) // Got a cpp project
            {
                return new NativeProjectSystem(vsProjectAdapter, nuGetProjectContext);
            }

            // Try to get a factory for the project type guid
            foreach (var guid in guids)
            {
                IMSBuildNuGetProjectSystemThunk factory;
                if (_factories.TryGetValue(guid, out factory))
                {
                    return factory(vsProjectAdapter, nuGetProjectContext);
                }
            }

            // Fall back to the default if we have no special project types
            return new VsMSBuildProjectSystem(vsProjectAdapter, nuGetProjectContext);
        }
    }
}
