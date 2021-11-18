// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;

namespace Yarp.ServiceFabric.ServiceFabricIntegration
{
    /// <summary>
    /// Wraps a Service Type Name string with added type safety to prevent the value from being misused.
    /// </summary>
    [DebuggerStepThrough]
    public struct ServiceTypeNameKey : IEquatable<ServiceTypeNameKey>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceTypeNameKey"/> struct.
        /// </summary>
        public ServiceTypeNameKey(string serviceTypeName)
        {
            this.Value = serviceTypeName;
        }

        /// <summary>
        /// The wrapped value.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Implicit conversion to string.
        /// </summary>
        public static implicit operator string(ServiceTypeNameKey self)
        {
            return self.Value;
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(ServiceTypeNameKey a, ServiceTypeNameKey b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(ServiceTypeNameKey a, ServiceTypeNameKey b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public bool Equals(ServiceTypeNameKey other)
        {
            return this.Value == other.Value;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is ServiceTypeNameKey other)
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
