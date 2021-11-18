// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Yarp.ServiceFabric.Common.Util
{
    /// <summary>
    /// Provides a method to compare 2 string dictionaries for value equality.
    /// </summary>
    public static class DictionaryEqualityUtil
    {
        /// <summary>
        /// Compares two dictionaries.
        /// </summary>
        public static bool AreDictionariesEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var targetValue) || targetValue != kvp.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }
}