﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Commands;
using NuGet.Frameworks;

namespace NuGet.VisualStudio
{
    public static class EnvDTEProjectInfoUtility
    {
        #region Constants and Statics

        public const string WebConfig = "web.config";
        public const string AppConfig = "app.config";
        public const string FullPath = "FullPath";
        public const string ProjectDirectory = "ProjectDirectory";
        public const string TargetPlatformIdentifier = "TargetPlatformIdentifier";
        public const string TargetPlatformVersion = "TargetPlatformVersion";
        public const string TargetPlatformMinVersion = "TargetPlatformMinVersion";
        public const string TargetFrameworkMoniker = "TargetFrameworkMoniker";

        #endregion // Constants and Statics

        #region Get Project Information

        /// <summary>
        /// Returns the full path including the project file name.
        /// </summary>
        public static string GetFullProjectPath(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Debug.Assert(envDTEProject != null);
            if (IsUnloaded(envDTEProject))
            {
                // Find the project file path from the UniqueName which contains the file path 
                // relative to the solution directory for unloaded projects.
                var solutionDirectory = Path.GetDirectoryName(envDTEProject.DTE.Solution.FullName);
                return Path.Combine(solutionDirectory, envDTEProject.UniqueName);
            }

            // FullPath
            var fullPath = GetPotentialFullPathOrNull(GetPropertyValue<string>(envDTEProject, FullPath));

            if (fullPath != null)
            {
                return fullPath;
            }

            // FullName
            var fullName = GetPotentialFullPathOrNull(envDTEProject.FullName);

            if (fullName != null)
            {
                return fullName;
            }

            return null;
        }

        /// <summary>
        /// Returns the full path of the project directory.
        /// </summary>
        /// <param name="envDTEProject">The project.</param>
        /// <returns>The full path of the project directory.</returns>
        public static string GetFullPath(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Debug.Assert(envDTEProject != null);
            if (IsUnloaded(envDTEProject))
            {
                // To get the directory of an unloaded project, we use the UniqueName property,
                // which is the path of the project file relative to the solution directory.
                var solutionDirectory = Path.GetDirectoryName(envDTEProject.DTE.Solution.FullName);
                var projectFileFullPath = Path.Combine(solutionDirectory, envDTEProject.UniqueName);
                return Path.GetDirectoryName(projectFileFullPath);
            }

            // Attempt to determine the project path using the available EnvDTE.Project properties.
            // Project systems using async load such as CPS may not have all properties populated 
            // for start up scenarios such as VS Templates. In these cases we need to fallback 
            // until we can find one containing the full path.

            // FullPath
            string fullPath = GetPropertyValue<string>(envDTEProject, FullPath);

            if (!String.IsNullOrEmpty(fullPath))
            {
                // Some Project System implementations (JS metro app) return the project 
                // file as FullPath. We only need the parent directory
                if (File.Exists(fullPath))
                {
                    return Path.GetDirectoryName(fullPath);
                }

                return fullPath;
            }

            // C++ projects do not have FullPath property, but do have ProjectDirectory one.
            string projectDirectory = GetPropertyValue<string>(envDTEProject, ProjectDirectory);

            if (!String.IsNullOrEmpty(projectDirectory))
            {
                return projectDirectory;
            }

            // FullName
            if (!String.IsNullOrEmpty(envDTEProject.FullName))
            {
                return Path.GetDirectoryName(envDTEProject.FullName);
            }

            Debug.Fail("Unable to find the project path");

            return null;
        }

        public static bool IsUnloaded(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return VsProjectTypes.UnloadedProjectTypeGuid.Equals(envDTEProject.Kind, StringComparison.OrdinalIgnoreCase);
        }

        public static EnvDTE.Project GetActiveProject(IVsMonitorSelection vsMonitorSelection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IntPtr ppHier = IntPtr.Zero;
            uint pitemid;
            IVsMultiItemSelect ppMIS;
            IntPtr ppSC = IntPtr.Zero;

            try
            {
                vsMonitorSelection.GetCurrentSelection(out ppHier, out pitemid, out ppMIS, out ppSC);

                if (ppHier == IntPtr.Zero)
                {
                    return null;
                }

                // multiple items are selected.
                if (pitemid == (uint)VSConstants.VSITEMID.Selection)
                {
                    return null;
                }

                IVsHierarchy hierarchy = Marshal.GetTypedObjectForIUnknown(ppHier, typeof(IVsHierarchy)) as IVsHierarchy;
                if (hierarchy != null)
                {
                    object project;
                    if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out project) >= 0)
                    {
                        return project as EnvDTE.Project;
                    }
                }

                return null;
            }
            finally
            {
                if (ppHier != IntPtr.Zero)
                {
                    Marshal.Release(ppHier);
                }
                if (ppSC != IntPtr.Zero)
                {
                    Marshal.Release(ppSC);
                }
            }
        }

        private static T GetPropertyValue<T>(EnvDTE.Project envDTEProject, string propertyName)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            Debug.Assert(envDTEProject != null);
            if (envDTEProject.Properties == null)
            {
                // this happens in unit tests
                return default(T);
            }

            try
            {
                EnvDTE.Property property = envDTEProject.Properties.Item(propertyName);
                if (property != null)
                {
                    return (T)property.Value;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidCastException)
            {
            }
            return default(T);
        }

        /// <summary>
        /// Returns the full path of the packages config file associated with the project.
        /// </summary>
        /// <param name="envDTEProject">The project.</param>
        public static string GetPackagesConfigFullPath(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Debug.Assert(envDTEProject != null);
            var projectDirectory = GetFullPath(envDTEProject);

            var packagesConfigFullPath = Path.Combine(
                projectDirectory ?? String.Empty,
                ProjectManagement.Constants.PackageReferenceFile);

            return packagesConfigFullPath;
        }

        public static string GetName(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string name = envDTEProject.Name;
            if (IsJavaScriptProject(envDTEProject))
            {
                // The JavaScript project initially returns a "(loading..)" suffix to the project Name.
                // Need to get rid of it for the rest of NuGet to work properly.
                // TODO: Follow up with the VS team to see if this will be fixed eventually
                const string suffix = " (loading...)";
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                }
            }
            return name;
        }

        public static string GetDisplayName(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string name = GetCustomUniqueName(envDTEProject);
            if (IsWebSite(envDTEProject))
            {
                name = PathHelper.SmartTruncate(name, 40);
            }
            return name;
        }

        public static string GetUniqueName(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (IsWixProject(envDTEProject))
            {
                // Wix project doesn't offer UniqueName property
                return envDTEProject.FullName;
            }

            try
            {
                return envDTEProject.UniqueName;
            }
            catch (COMException)
            {
                return envDTEProject.FullName;
            }
        }

        /// <summary>
        /// Returns the unique name of the specified project including all solution folder names containing it.
        /// </summary>
        /// <remarks>
        /// This is different from the DTE Project.UniqueName property, which is the absolute path to the project file.
        /// </remarks>
        public static string GetCustomUniqueName(EnvDTE.Project envDTEProject)
        {
            return NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate { return await GetCustomUniqueNameAsync(envDTEProject); });
        }

        public static async Task<string> GetCustomUniqueNameAsync(EnvDTE.Project envDTEProject)
        {
            return await NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (IsWebSite(envDTEProject))
                {
                    // website projects always have unique name
                    return envDTEProject.Name;
                }
                Stack<string> nameParts = new Stack<string>();

                EnvDTE.Project cursor = envDTEProject;
                nameParts.Push(GetName(cursor));

                // walk up till the solution root
                while (cursor.ParentProjectItem != null
                       && cursor.ParentProjectItem.ContainingProject != null)
                {
                    cursor = cursor.ParentProjectItem.ContainingProject;
                    nameParts.Push(GetName(cursor));
                }

                return String.Join("\\", nameParts);
            });
            
        }

        public static NuGetFramework GetTargetNuGetFramework(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var targetFrameworkMoniker = GetTargetFrameworkString(envDTEProject);

            if (!string.IsNullOrEmpty(targetFrameworkMoniker))
            {
                return NuGetFramework.Parse(targetFrameworkMoniker);
            }

            return NuGetFramework.UnsupportedFramework;
        }

        /// <summary>
        /// Determine the project framework string based on the project properties.
        /// </summary>
        public static string GetTargetFrameworkString(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (envDTEProject == null)
            {
                return null;
            }

            var projectPath = GetFullProjectPath(envDTEProject);
            var platformIdentifier = GetPropertyValue<string>(envDTEProject, TargetPlatformIdentifier);
            var platformVersion = GetPropertyValue<string>(envDTEProject, TargetPlatformVersion);
            var platformMinVersion = GetPropertyValue<string>(envDTEProject, TargetPlatformMinVersion);
            var targetFrameworkMoniker = GetPropertyValue<string>(envDTEProject, TargetFrameworkMoniker);
            var isManagementPackProject = IsManagementPackProject(envDTEProject);
            var isXnaWindowsPhoneProject = IsXnaWindowsPhoneProject(envDTEProject);

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
                isManagementPackProject: isManagementPackProject,
                isXnaWindowsPhoneProject: isXnaWindowsPhoneProject);

            return frameworkStrings.FirstOrDefault();
        }

        // TODO: Return null for library projects
        public static string GetConfigurationFile(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return IsWebProject(envDTEProject) ? WebConfig : AppConfig;
        }

        public static FrameworkName GetDotNetFrameworkName(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string targetFrameworkMoniker = GetTargetFrameworkString(envDTEProject);
            if (!String.IsNullOrEmpty(targetFrameworkMoniker))
            {
                return new FrameworkName(targetFrameworkMoniker);
            }

            return null;
        }

        private class PathComparer : IEqualityComparer<string>
        {
            public static readonly PathComparer Default = new PathComparer();

            public bool Equals(string x, string y)
            {
                return Path.GetFileName(x).Equals(Path.GetFileName(y), StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(string obj)
            {
                return Path.GetFileName(obj).ToLowerInvariant().GetHashCode();
            }
        }

        /// <summary>
        /// A DTE specific helper method that validates a path to ensure that it 
        /// could be for a file as opposed to a URL or other invalid path, and
        /// not for a directory. This is used to help determine if a value returned
        /// from DTE is a directory or file, since the file may still be in 
        /// memory and not yet written to disk File.Exists will not work.
        /// </summary>
        private static string GetPotentialFullPathOrNull(string path)
        {
            string fullPath = null;

            try
            {
                if (!string.IsNullOrEmpty(path))
                {
                    // Attempt to get the full path
                    fullPath = Path.GetFullPath(path);

                    // Some project systems may return a directory for the file path. 
                    // Directories usually exist even when the in-memory files have not yet 
                    // been written, so we can try to detect obvious non-files here.
                    // WebSites and Win JS projects can return a directory instead of the project file path.
                    if (Directory.Exists(fullPath))
                    {
                        // Ignore directories
                        fullPath = null;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException
                || ex is NotSupportedException
                || ex is PathTooLongException
                || ex is SecurityException)
            {
                // Ignore invalid paths
                // This can occur if the path was a URL
            }

            return fullPath;
        }

        #endregion // Get Project Information

        #region Check Project Types

        public static bool IsJavaScriptProject(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            return envDTEProject != null && VsProjectTypes.JsProjectTypeGuid.Equals(envDTEProject.Kind, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManagementPackProject(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            return envDTEProject != null && VsProjectTypes.ManagementPackProjectTypeGuid.Equals(envDTEProject.Kind, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsXnaWindowsPhoneProject(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            // XNA projects will have this property set
            const string xnaPropertyValue = "Microsoft.Xna.GameStudio.CodeProject.WindowsPhoneProjectPropertiesExtender.XnaRefreshLevel";
            return envDTEProject != null &&
                   "Windows Phone OS 7.1".Equals(GetPropertyValue<string>(envDTEProject, xnaPropertyValue), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNativeProject(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            return envDTEProject != null
                && VsProjectTypes.CppProjectTypeGuid.Equals(envDTEProject.Kind, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWebProject(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            string[] types = VsHierarchyUtility.GetProjectTypeGuids(envDTEProject);
            return types.Contains(VsProjectTypes.WebSiteProjectTypeGuid, StringComparer.OrdinalIgnoreCase) ||
                   types.Contains(VsProjectTypes.WebApplicationProjectTypeGuid, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsWebSite(EnvDTE.Project envDTEProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return envDTEProject.Kind != null && envDTEProject.Kind.Equals(VsProjectTypes.WebSiteProjectTypeGuid, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWindowsStoreApp(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            string[] types = VsHierarchyUtility.GetProjectTypeGuids(envDTEProject);
            return types.Contains(VsProjectTypes.WindowsStoreProjectTypeGuid, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsWixProject(EnvDTE.Project envDTEProject)
        {
            Debug.Assert(ThreadHelper.CheckAccess());

            return envDTEProject.Kind != null && envDTEProject.Kind.Equals(VsProjectTypes.WixProjectTypeGuid, StringComparison.OrdinalIgnoreCase);
        }

        #endregion // Check Project Types
    }
}
