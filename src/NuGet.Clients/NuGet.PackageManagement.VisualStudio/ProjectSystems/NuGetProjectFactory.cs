﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using NuGet.ProjectManagement;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// Project system factory imlemented as a composite provider chaining calls to other providers.
    /// </summary>
    [Export(typeof(NuGetProjectFactory))]
    internal sealed class NuGetProjectFactory
    {
        private readonly INuGetProjectProvider[] _providers;

        [ImportingConstructor]
        public NuGetProjectFactory(
            [ImportMany(typeof(INuGetProjectProvider))]
            IEnumerable<Lazy<INuGetProjectProvider, IOrderable>> providers)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            _providers = Orderer
                .Order(providers)
                .Select(p => p.Value)
                .ToArray();
        }

        public bool TryCreateNuGetProject(IVsProjectAdapter vsProjectAdapter, ProjectSystemProviderContext context, out NuGetProject result)
        {
            if (vsProjectAdapter == null)
            {
                throw new ArgumentNullException(nameof(vsProjectAdapter));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            var exceptions = new List<Exception>();
            result = _providers
                .Select(p =>
                {
                    try
                    {
                        NuGetProject nuGetProject;
                        if (p.TryCreateNuGetProject(vsProjectAdapter, context, out nuGetProject))
                        {
                            return nuGetProject;
                        }
                    }
                    catch (Exception e)
                    {
                        // Ignore failures. If this method returns null, the problem falls 
                        // into one of the other NuGet project types.
                        exceptions.Add(e);
                    }

                    return null;
                })
                .FirstOrDefault(p => p != null);

            if (result == null)
            {
                exceptions.ForEach(ExceptionHelper.WriteWarningToActivityLog);
            }

            return result != null;
        }
    }
}
