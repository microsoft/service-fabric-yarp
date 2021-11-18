// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Wraps a Service Name uri with added type safety to prevent the value from being misused.
    /// </summary>
    [DebuggerStepThrough]
    public struct ServiceNameKey : IEquatable<ServiceNameKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceNameKey"/> struct.
        /// </summary>
        public ServiceNameKey(Uri serviceName)
        {
            this.Value = serviceName;
        }

        /// <summary>
        /// The wrapped value.
        /// </summary>
        public Uri Value { get; set; }

        /// <summary>
        /// Implicit conversion to string.
        /// </summary>
        public static implicit operator Uri(ServiceNameKey self)
        {
            return self.Value;
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ServiceNameKey a, ServiceNameKey b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ServiceNameKey a, ServiceNameKey b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public bool Equals(ServiceNameKey other)
        {
            return this.Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is ServiceNameKey other)
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
