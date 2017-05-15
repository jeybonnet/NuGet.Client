// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// A nuget aware project system containing a .json file instead of a packages.config file
    /// </summary>
    internal class VsProjectJsonNuGetProject : ProjectJsonNuGetProject
    {
        private readonly IVsProjectAdapter _vsProjectAdapter;
        private readonly Lazy<IScriptExecutor> _scriptExecutor;

        public VsProjectJsonNuGetProject(
            string jsonConfigPath,
            string msbuildProjectFilePath,
            IVsProjectAdapter vsProjectAdapter,
            INuGetProjectServices projectServices)
            : base(jsonConfigPath, msbuildProjectFilePath)
        {
            Assumes.Present(vsProjectAdapter);
            Assumes.Present(projectServices);

            _vsProjectAdapter = vsProjectAdapter;

            _scriptExecutor = new Lazy<IScriptExecutor>(
                () => projectServices.GetService<IScriptExecutor>());

            InternalMetadata.Add(NuGetProjectMetadataKeys.ProjectId, _vsProjectAdapter.ProjectId);
            InternalMetadata.Add(NuGetProjectMetadataKeys.UniqueName, _vsProjectAdapter.CustomUniqueName);

            ProjectServices = projectServices;
        }

        public override Task<bool> ExecuteInitScriptAsync(
            PackageIdentity identity,
            string packageInstallPath,
            INuGetProjectContext projectContext,
            bool throwOnFailure)
        {
            var scriptExecutor = _scriptExecutor.Value;
            Assumes.Present(scriptExecutor);

            return ScriptExecutorUtil.ExecuteScriptAsync(
                identity,
                packageInstallPath,
                projectContext,
                scriptExecutor,
                _vsProjectAdapter.Project,
                throwOnFailure);
        }
    }
}
