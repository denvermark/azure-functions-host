﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public interface IFunctionsSyncManager
    {
        Task<(bool success, string error)> TrySyncTriggersAsync();
    }
}