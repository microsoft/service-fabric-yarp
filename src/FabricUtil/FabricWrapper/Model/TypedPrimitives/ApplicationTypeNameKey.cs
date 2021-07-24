// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace IslandGateway.ServiceFabricIntegration
{
    /// <summary>
    /// Wraps an Application Type Name string with added type safety to prevent the value from being misused.
    /// </summary>
    [DebuggerStepThrough]
    public struct ApplicationTypeNameKey : IEquatable<ApplicationTypeNameKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationTypeNameKey"/> struct.
        /// </summary>
        public ApplicationTypeNameKey(string applicationTypeName)
        {
            this.Value = applicationTypeName;
        }

        /// <summary>
        /// The wrapped value.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Implicit conversion to string.
        /// </summary>
        public static implicit operator string(ApplicationTypeNameKey self)
        {
            return self.Value;
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ApplicationTypeNameKey a, ApplicationTypeNameKey b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ApplicationTypeNameKey a, ApplicationTypeNameKey b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public bool Equals(ApplicationTypeNameKey other)
        {
            return this.Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is ApplicationTypeNameKey other)
            {
                return this.Equals(other);
            }

            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return this.Value?.GetHashCode() ?? 0;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return this.Value;
        }
    }
}
