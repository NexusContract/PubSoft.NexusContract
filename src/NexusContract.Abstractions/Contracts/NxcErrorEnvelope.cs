// Copyright (c) 2025-2026 NexusContract. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;

namespace NexusContract.Abstractions.Contracts
{
    /// <summary>
    /// Standard error envelope used across server and client to convey structured NXC errors.
    /// JSON shape: { "code": "NXC101", "message": "...", "data": { ... } }
    /// Note: kept as a plain POCO (no Json-specific attributes) for maximum framework compatibility.
    /// </summary>
    public sealed class NxcErrorEnvelope
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, object>? Data { get; set; }
    }
}


