// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Wraps an Application Type Version string with added type safety to prevent the value from being misused.
    /// </summary>
    [DebuggerStepThrough]
    public struct ApplicationTypeVersionKey : IEquatable<ApplicationTypeVersionKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationTypeVersionKey"/> struct.
        /// </summary>
        public ApplicationTypeVersionKey(string applicationTypeVersion)
        {
            this.Value = applicationTypeVersion;
        }

        /// <summary>
        /// The wrapped value.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Implicit conversion to string.
        /// </summary>
        public static implicit operator string(ApplicationTypeVersionKey self)
        {
            return self.Value;
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ApplicationTypeVersionKey a, ApplicationTypeVersionKey b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ApplicationTypeVersionKey a, ApplicationTypeVersionKey b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public bool Equals(ApplicationTypeVersionKey other)
        {
            return this.Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is ApplicationTypeVersionKey other)
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
