// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Topology
{
    /// <summary>
    /// Keeps track of which services are dirty and need to be rediscovered.
    /// This class is thread safe.
    /// </summary>
    internal class DirtyServicesTracker
    {
        private readonly object lockObject = new object();
        private HashSet<ServiceNameKey> dirtyServices = new HashSet<ServiceNameKey>();

        /// <summary>
        /// Interface to rollback changes made to the <see cref="DirtyServicesTracker"/>.
        /// </summary>
        public interface IRollbacker
        {
            /// <summary>
            /// Undoes the operation that had been started, re-marking any services that had been unmarked, making them dirty again.
            /// </summary>
            void Rollback();
        }

        public List<ServiceNameKey> GetSnapshot()
        {
            var result = new List<ServiceNameKey>();
            lock (this.lockObject)
            {
                foreach (var serviceName in this.dirtyServices)
                {
                    result.Add(serviceName);
                }
            }

            return result;
        }

        public bool Mark(ServiceNameKey serviceName)
        {
            lock (this.lockObject)
            {
                return this.dirtyServices.Add(serviceName);
            }
        }

        public IRollbacker Unmark(List<ServiceNameKey> serviceNames)
        {
            _ = serviceNames ?? throw new ArgumentNullException(nameof(serviceNames));

            var toRollback = new List<ServiceNameKey>();
            lock (this.lockObject)
            {
                foreach (var serviceName in serviceNames)
                {
                    if (this.dirtyServices.Remove(serviceName))
                    {
                        toRollback.Add(serviceName);
                    }
                }
            }

            return new ItemsRollbacker(this, toRollback);
        }

        public IRollbacker UnmarkAll()
        {
            HashSet<ServiceNameKey> toRollback;
            lock (this.lockObject)
            {
                toRollback = this.dirtyServices;
                this.dirtyServices = new HashSet<ServiceNameKey>();
            }

            return new ClearRollbacker(this, toRollback);
        }

        private sealed class ItemsRollbacker : IRollbacker
        {
            private readonly DirtyServicesTracker owner;
            private readonly List<ServiceNameKey> toRollback;

            public ItemsRollbacker(DirtyServicesTracker owner, List<ServiceNameKey> toRollback)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                this.toRollback = toRollback ?? throw new ArgumentNullException(nameof(toRollback));
            }

            public void Rollback()
            {
                lock (this.owner.lockObject)
                {
                    foreach (var serviceName in this.toRollback)
                    {
                        this.owner.dirtyServices.Add(serviceName);
                    }
                }
            }
        }

        private sealed class ClearRollbacker : IRollbacker
        {
            private readonly DirtyServicesTracker owner;
            private readonly HashSet<ServiceNameKey> oldDirtyItems;

            public ClearRollbacker(DirtyServicesTracker owner, HashSet<ServiceNameKey> oldDirtyItems)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                this.oldDirtyItems = oldDirtyItems ?? throw new ArgumentNullException(nameof(oldDirtyItems));
            }

            public void Rollback()
            {
                lock (this.owner.lockObject)
                {
                    foreach (var serviceName in this.oldDirtyItems)
                    {
                        this.owner.dirtyServices.Add(serviceName);
                    }
                }
            }
        }
    }
}
