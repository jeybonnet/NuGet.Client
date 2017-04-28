﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;
using NuGet.ProjectManagement;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    [Export(typeof(IProjectSystemProvider))]
    [Name(nameof(ProjectKNuGetProjectProvider))]
    public class ProjectKNuGetProjectProvider : IProjectSystemProvider
    {
        public bool TryCreateNuGetProject(IVsProjectAdapter project, ProjectSystemProviderContext context, out NuGetProject result)
        {
            Assumes.Present(project);
            Assumes.Present(context);

            result = null;

            ThreadHelper.ThrowIfNotOnUIThread();

            if (project.IsLoadDeferred)
            {
                return false;
            }

            var projectK = EnvDTEProjectUtility.GetProjectSystemService(project.Project);
            if (projectK == null)
            {
                return false;
            }

            result = new ProjectKNuGetProject(
                projectK,
                project.ProjectName,
                project.CustomUniqueName,
                project.ProjectId);

            return true;
        }
    }
}
