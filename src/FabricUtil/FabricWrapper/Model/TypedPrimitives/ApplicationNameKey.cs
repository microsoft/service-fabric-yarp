// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Wraps a Application Name uri with added type safety to prevent the value from being misused.
    /// </summary>
    [DebuggerStepThrough]
    public struct ApplicationNameKey : IEquatable<ApplicationNameKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationNameKey"/> struct.
        /// </summary>
        public ApplicationNameKey(Uri applicationName)
        {
            this.Value = applicationName;
        }

        /// <summary>
        /// The wrapped value.
        /// </summary>
        public Uri Value { get; set; }

        /// <summary>
        /// Implicit conversion to uri.
        /// </summary>
        public static implicit operator Uri(ApplicationNameKey self)
        {
            return self.Value;
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ApplicationNameKey a, ApplicationNameKey b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ApplicationNameKey a, ApplicationNameKey b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public bool Equals(ApplicationNameKey other)
        {
            return this.Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is ApplicationNameKey other)
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
            return this.Value?.ToString();
        }
    }
}
