// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    internal static class ListEqualityUtil
    {
        /// <summary>
        /// Combines two dictionaries into a new one.
        /// Values in dictionaries that appear later override values from previous dictionaries.
        /// This is somewhat similar to the spread operator in JavaScript.
        /// </summary>
        public static bool AreListsEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T> comparer)
        {
            _ = a ?? throw new ArgumentNullException(nameof(a));
            _ = b ?? throw new ArgumentNullException(nameof(b));
            _ = comparer ?? throw new ArgumentNullException(nameof(comparer));

            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!comparer.Equals(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
