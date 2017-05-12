// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    internal static class ProjectTypesConstants
    {
        public static readonly HashSet<string> SupportedProjectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                VsProjectTypes.WebSiteProjectTypeGuid,
                VsProjectTypes.CsharpProjectTypeGuid,
                VsProjectTypes.VbProjectTypeGuid,
                VsProjectTypes.CppProjectTypeGuid,
                VsProjectTypes.JsProjectTypeGuid,
                VsProjectTypes.FsharpProjectTypeGuid,
                VsProjectTypes.NemerleProjectTypeGuid,
                VsProjectTypes.WixProjectTypeGuid,
                VsProjectTypes.SynergexProjectTypeGuid,
                VsProjectTypes.NomadForVisualStudioProjectTypeGuid,
                VsProjectTypes.TDSProjectTypeGuid,
                VsProjectTypes.DxJsProjectTypeGuid,
                VsProjectTypes.DeploymentProjectTypeGuid,
                VsProjectTypes.CosmosProjectTypeGuid,
                VsProjectTypes.ManagementPackProjectTypeGuid,
            };

        public static readonly HashSet<string> UnsupportedProjectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                VsProjectTypes.LightSwitchProjectTypeGuid,
                VsProjectTypes.InstallShieldLimitedEditionTypeGuid,
            };

        // List of project types that cannot have binding redirects added
        public static readonly string[] UnsupportedProjectTypesForBindingRedirects =
            {
                VsProjectTypes.WixProjectTypeGuid,
                VsProjectTypes.JsProjectTypeGuid,
                VsProjectTypes.NemerleProjectTypeGuid,
                VsProjectTypes.CppProjectTypeGuid,
                VsProjectTypes.SynergexProjectTypeGuid,
                VsProjectTypes.NomadForVisualStudioProjectTypeGuid,
                VsProjectTypes.DxJsProjectTypeGuid,
                VsProjectTypes.CosmosProjectTypeGuid,
            };

        // List of project types that cannot have references added to them
        public static readonly string[] UnsupportedProjectTypesForAddingReferences =
            {
                VsProjectTypes.WixProjectTypeGuid,
                VsProjectTypes.CppProjectTypeGuid,
            };
    }
}
