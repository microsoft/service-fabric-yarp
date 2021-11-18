// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    internal static class DictionaryUtil
    {
        /// <summary>
        /// Combines two dictionaries into a new one.
        /// Values in dictionaries that appear later override values from previous dictionaries.
        /// This is somewhat similar to the spread operator in JavaScript.
        /// </summary>
        public static Dictionary<string, string> CombineDictionaries(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b, StringComparer comparer)
        {
            _ = a ?? throw new ArgumentNullException(nameof(a));
            _ = b ?? throw new ArgumentNullException(nameof(b));
            _ = comparer ?? throw new ArgumentNullException(nameof(comparer));

            var result = new Dictionary<string, string>(a, comparer);
            foreach (var kvp in b)
            {
                result[kvp.Key] = kvp.Value;
            }

            return result;
        }

        public static Dictionary<TKey, TValue> ShallowClone<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> source)
        {
            _ = source ?? throw new ArgumentNullException(nameof(source));
            return new Dictionary<TKey, TValue>(source);
        }
    }
}
