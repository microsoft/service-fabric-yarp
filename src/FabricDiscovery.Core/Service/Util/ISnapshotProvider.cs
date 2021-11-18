// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.Primitives;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    /// <summary>
    /// Provides a method to get the current snapshot of some configuration object.
    /// </summary>
    public interface ISnapshotProvider<T>
    {
        /// <summary>
        /// Gets a topology snapshot which includes a notification mechanism to detect changes.
        /// </summary>
        public Snapshot<T> GetSnapshot();
    }

    /// <summary>
    /// Represents a snapshot of some configuration object.
    /// </summary>
    public class Snapshot<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Snapshot{T}"/> class.
        /// </summary>
        public Snapshot(T value, IChangeToken changeToken)
        {
            this.Value = value;
            this.ChangeToken = changeToken ?? throw new ArgumentNullException(nameof(changeToken));
        }

        /// <summary>
        /// The current value stored in this snapshot.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Change notification mechanism.
        /// </summary>
        public IChangeToken ChangeToken { get; }
    }
}
