// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ServiceFabric.Common.Util;
using Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Util
{
    /// <summary>
    /// Helper class to parse configuration labels into actual objects.
    /// </summary>
    internal static class LabelsParserV2
    {
        // Look for route IDs
        private const string RoutesLabelsPrefix = "Yarp.Routes.";

        /// <summary>
        /// Require all route names to only contain alphanumerical characters, underscores or hyphens.
        /// </summary>
        private static readonly Regex AllowedRouteNamesRegex = new Regex("^[a-zA-Z0-9_-]+$");

        /// <summary>
        /// Requires all header match names to follow the .[0]. pattern to simulate indexing in an array.
        /// </summary>
        private static readonly Regex AllowedHeaderNamesRegex = new Regex(@"^\[\d\d*\]$", RegexOptions.Compiled);

        /// <summary>
        /// Requires all transform names to follow the .[0]. pattern to simulate indexing in an array.
        /// </summary>
        private static readonly Regex AllowedTransformNamesRegex = new Regex(@"^\[\d\d*\]$", RegexOptions.Compiled);

        /// <summary>
        /// Requires all query parameter names to follow the .[0]. pattern to simulate indexing in an array.
        /// </summary>
        private static readonly Regex AllowedQueryParameterNamesRegex = new Regex(@"^\[\d\d*\]$", RegexOptions.Compiled);

        internal static bool IsEnabled(IReadOnlyDictionary<string, string> labels)
        {
            return string.Equals(labels.GetValueOrDefault("Yarp.Enable", null), "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool UseDynamicOverrides(IReadOnlyDictionary<string, string> labels)
        {
            return string.Equals(labels.GetValueOrDefault("Yarp.EnableDynamicOverrides", null), "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<RouteConfig> BuildRoutes(SFYarpBackendService backendService, List<string> errors)
        {
            IReadOnlyDictionary<string, string> labels = backendService.FinalEffectiveLabels;
            Uri serviceName = backendService.FabricService.Service.ServiceName;

            var routesNames = new Dictionary<StringSegment, string>();

            foreach (var kvp in labels)
            {
                if (kvp.Key.Length > RoutesLabelsPrefix.Length && kvp.Key.StartsWith(RoutesLabelsPrefix, StringComparison.Ordinal))
                {
                    var suffix = new StringSegment(kvp.Key).Subsegment(RoutesLabelsPrefix.Length);
                    int routeNameLength = suffix.IndexOf('.');
                    if (routeNameLength == -1)
                    {
                        // No route name encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                        continue;
                    }

                    var routeNameSegment = suffix.Subsegment(0, routeNameLength + 1);
                    if (routesNames.ContainsKey(routeNameSegment))
                    {
                        continue;
                    }

                    var routeName = routeNameSegment.Subsegment(0, routeNameSegment.Length - 1).ToString();
                    if (!AllowedRouteNamesRegex.IsMatch(routeName))
                    {
                        errors.Add($"Invalid route name '{routeName}', should only contain alphanumerical characters, underscores or hyphens.");
                        return null;
                    }

                    routesNames.Add(routeNameSegment, routeName);
                }
            }

            // Build the routes
            var routes = new List<RouteConfig>();
            string backendId = GetClusterId(serviceName, labels);
            foreach (var routeNamePair in routesNames)
            {
                string hosts = null;
                string methods = null;
                string path = null;
                int? order = null;
                string authorizationPolicy = null;
                string corsPolicy = null;
                var metadata = new Dictionary<string, string>();
                var headerMatches = new Dictionary<string, RouteHeaderFields>();
                var queryMatches = new Dictionary<string, RouteQueryParameterFields>();
                var transforms = new Dictionary<string, Dictionary<string, string>>();

                foreach (var kvp in labels)
                {
                    if (!kvp.Key.StartsWith(RoutesLabelsPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var routeLabelKey = kvp.Key.AsSpan().Slice(RoutesLabelsPrefix.Length);

                    if (routeLabelKey.Length < routeNamePair.Key.Length || !routeLabelKey.StartsWith(routeNamePair.Key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    routeLabelKey = routeLabelKey.Slice(routeNamePair.Key.Length);

                    if (ContainsKey("Metadata.", routeLabelKey, out var keyRemainder))
                    {
                        metadata.Add(keyRemainder.ToString(), kvp.Value);
                    }
                    else if (ContainsKey("MatchHeaders.", routeLabelKey, out keyRemainder))
                    {
                        var headerIndexLength = keyRemainder.IndexOf('.');
                        if (headerIndexLength == -1)
                        {
                            // No header encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                            continue;
                        }
                        var headerIndex = keyRemainder.Slice(0, headerIndexLength).ToString();
                        if (!AllowedHeaderNamesRegex.IsMatch(headerIndex))
                        {
                            errors.Add($"Invalid header matching index '{headerIndex}', should be header index wrapped in square brackets.");
                        }
                        if (!headerMatches.ContainsKey(headerIndex))
                        {
                            headerMatches.Add(headerIndex, new RouteHeaderFields());
                        }

                        var propertyName = keyRemainder.Slice(headerIndexLength + 1);
                        if (propertyName.Equals("Name", StringComparison.Ordinal))
                        {
                            headerMatches[headerIndex].Name = kvp.Value;
                        }
                        else if (propertyName.Equals("Values", StringComparison.Ordinal))
                        {
#if NET
                            headerMatches[headerIndex].Values = kvp.Value.Split(',', StringSplitOptions.TrimEntries);
#elif NETCOREAPP3_1
                        headerMatches[headerIndex].Values = kvp.Value.Split(',').Select(val => val.Trim()).ToList();
#else
#error A target framework was added to the project and needs to be added to this condition.
#endif
                        }
                        else if (propertyName.Equals("IsCaseSensitive", StringComparison.Ordinal))
                        {
                            headerMatches[headerIndex].IsCaseSensitive = bool.Parse(kvp.Value);
                        }
                        else if (propertyName.Equals("Mode", StringComparison.Ordinal))
                        {
                            headerMatches[headerIndex].Mode = Enum.Parse<HeaderMatchMode>(kvp.Value);
                        }
                        else
                        {
                            errors.Add($"Invalid header matching property '{propertyName.ToString()}', only valid values are Name, Values, IsCaseSensitive and Mode.");
                        }
                    }
                    else if (ContainsKey("MatchQueries.", routeLabelKey, out keyRemainder))
                    {
                        var queryIndexLength = keyRemainder.IndexOf('.');
                        if (queryIndexLength == -1)
                        {
                            // No query encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                            continue;
                        }
                        var queryIndex = keyRemainder.Slice(0, queryIndexLength).ToString();
                        if (!AllowedQueryParameterNamesRegex.IsMatch(queryIndex))
                        {
                            errors.Add($"Invalid query matching index '{queryIndex}', should be query index wrapped in square brackets.");
                        }
                        if (!queryMatches.ContainsKey(queryIndex))
                        {
                            queryMatches.Add(queryIndex, new RouteQueryParameterFields());
                        }

                        var propertyName = keyRemainder.Slice(queryIndexLength + 1);
                        if (propertyName.Equals("Name", StringComparison.Ordinal))
                        {
                            queryMatches[queryIndex].Name = kvp.Value;
                        }
                        else if (propertyName.Equals("Values", StringComparison.Ordinal))
                        {
#if NET
                            queryMatches[queryIndex].Values = kvp.Value.Split(',', StringSplitOptions.TrimEntries);
#elif NETCOREAPP3_1
                        queryMatches[queryIndex].Values = kvp.Value.Split(',').Select(val => val.Trim()).ToList();
#else
#error A target framework was added to the project and needs to be added to this condition.
#endif
                        }
                        else if (propertyName.Equals("IsCaseSensitive", StringComparison.Ordinal))
                        {
                            queryMatches[queryIndex].IsCaseSensitive = bool.Parse(kvp.Value);
                        }
                        else if (propertyName.Equals("Mode", StringComparison.Ordinal))
                        {
                            queryMatches[queryIndex].Mode = Enum.Parse<QueryParameterMatchMode>(kvp.Value);
                        }
                        else
                        {
                            errors.Add($"Invalid query matching property '{propertyName.ToString()}', only valid values are Name, Values, IsCaseSensitive and Mode.");
                        }
                    }
                    else if (ContainsKey("Transforms.", routeLabelKey, out keyRemainder))
                    {
                        var transformNameLength = keyRemainder.IndexOf('.');
                        if (transformNameLength == -1)
                        {
                            // No transform index encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                            continue;
                        }
                        var transformName = keyRemainder.Slice(0, transformNameLength).ToString();
                        if (!AllowedTransformNamesRegex.IsMatch(transformName))
                        {
                            errors.Add($"Invalid transform index '{transformName}', should be transform index wrapped in square brackets.");
                        }
                        if (!transforms.ContainsKey(transformName))
                        {
                            transforms.Add(transformName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                        }
                        var propertyName = keyRemainder.Slice(transformNameLength + 1).ToString();
                        if (!transforms[transformName].ContainsKey(propertyName))
                        {
                            transforms[transformName].Add(propertyName, kvp.Value);
                        }
                        else
                        {
                            errors.Add($"A duplicate transformation property '{transformName}.{propertyName}' was found.");
                        }
                    }
                    else if (ContainsKey("Hosts", routeLabelKey, out _))
                    {
                        hosts = kvp.Value;
                    }
                    else if (ContainsKey("Path", routeLabelKey, out _))
                    {
                        path = kvp.Value;
                    }
                    else if (ContainsKey("Methods", routeLabelKey, out _))
                    {
                        methods = kvp.Value;
                    }
                    else if (ContainsKey("Order", routeLabelKey, out _))
                    {
                        order = ConvertLabelValue<int?>(kvp.Key, kvp.Value, errors);
                    }
                    else if (ContainsKey("AuthorizationPolicy", routeLabelKey, out _))
                    {
                        authorizationPolicy = kvp.Value;
                    }
                    else if (ContainsKey("CorsPolicy", routeLabelKey, out _))
                    {
                        corsPolicy = kvp.Value;
                    }
                }

                // Combine default path generation (/appName/serviceName) and user defined path from yarp label in service manifest.
                var finalPath = serviceName.AbsolutePath + path;

                // Stateful/stateless service with multiple partitions
                if (backendService.FabricService.Partitions.Count > 1)
                {
                    // Implicitly add transform to remove serviceName and partitionID before request is forwarded to stateful service
                    var transformCount = transforms.Count;
                    transforms.Add($"[{transformCount}]", new Dictionary<string, string> { { "PathRemovePrefix", serviceName.AbsolutePath } });
                    transforms.Add($"[{transformCount + 1}]", new Dictionary<string, string> { { "QueryRemoveParameter", "PartitionID" } });

                    foreach (var partition in backendService.FabricService.Partitions)
                    {
                        // Implicitly add queryMatch for partitionID to support sending request to a stateful service running on a specific partition
                        var partitionIdKey = $"[{queryMatches.Count}]";
                        queryMatches.Add(partitionIdKey, new RouteQueryParameterFields());
                        queryMatches[partitionIdKey].Name = "PartitionID";
                        queryMatches[partitionIdKey].Values = new List<string> { $"{partition.Partition.PartitionId}" };

                        var route = new RouteConfig
                        {
                            RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(partition.Partition.PartitionId.ToString())}:{Uri.EscapeDataString(routeNamePair.Value)}",

                            Match = new RouteMatch
                            {
                                Hosts = SplitHosts(hosts),
                                Path = finalPath,
                                Methods = SplitMethods(methods),
                                QueryParameters = queryMatches.Count > 0 ? queryMatches.Select(qm => new RouteQueryParameter()
                                {
                                    Name = qm.Value.Name,
                                    Values = qm.Value.Values,
                                    Mode = qm.Value.Mode,
                                    IsCaseSensitive = qm.Value.IsCaseSensitive,
                                }).ToArray() : null,
                                Headers = headerMatches.Count > 0 ? headerMatches.Select(hm => new RouteHeader()
                                {
                                    Name = hm.Value.Name,
                                    Values = hm.Value.Values,
                                    Mode = hm.Value.Mode,
                                    IsCaseSensitive = hm.Value.IsCaseSensitive,
                                }).ToArray() : null,
                            },

                            Order = order,
                            ClusterId = backendId + "/" + partition.Partition.PartitionId,
                            Metadata = metadata,
                            AuthorizationPolicy = authorizationPolicy,
                            CorsPolicy = corsPolicy,
                            Transforms = transforms.Count > 0 ? transforms.Select(tr => tr.Value).ToList().AsReadOnly() : null,
                        };

                        // Remove recently added query match for partitionID after Route is built since its unique for each Route
                        queryMatches.Remove(partitionIdKey);

                        routes.Add(route);
                    }
                }
                else
                {
                    // Implicitly add transform to remove serviceName before request is forwarded to stateless service
                    transforms.Add($"[{transforms.Count}]", new Dictionary<string, string> { { "PathRemovePrefix", serviceName.AbsolutePath } });

                    // Stateless/stateful service with singleton partition
                    var route = new RouteConfig
                    {
                        RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(routeNamePair.Value)}",

                        Match = new RouteMatch
                        {
                            Hosts = SplitHosts(hosts),
                            Path = finalPath,
                            Methods = SplitMethods(methods),
                            QueryParameters = queryMatches.Count > 0 ? queryMatches.Select(qm => new RouteQueryParameter()
                            {
                                Name = qm.Value.Name,
                                Values = qm.Value.Values,
                                Mode = qm.Value.Mode,
                                IsCaseSensitive = qm.Value.IsCaseSensitive,
                            }).ToArray() : null,
                            Headers = headerMatches.Count > 0 ? headerMatches.Select(hm => new RouteHeader()
                            {
                                Name = hm.Value.Name,
                                Values = hm.Value.Values,
                                Mode = hm.Value.Mode,
                                IsCaseSensitive = hm.Value.IsCaseSensitive,
                            }).ToArray() : null,
                        },

                        Order = order,
                        ClusterId = backendId,
                        Metadata = metadata,
                        AuthorizationPolicy = authorizationPolicy,
                        CorsPolicy = corsPolicy,
                        Transforms = transforms.Count > 0 ? transforms.Select(tr => tr.Value).ToList().AsReadOnly() : null,
                    };

                    if (errors.Count > 0)
                    {
                        // Reading a label above may have failed due to bad user inputs.
                        // If so, abort and report the error.
                        return null;
                    }

                    routes.Add(route);
                }
            }

            // Sort discovered routes so that final output is deterministic.
            // Note that we shouldn't need this to achieve E2E determinism, but it helps in case there are bugs elsewhere.
            routes.Sort((a, b) => string.Compare(a.RouteId, b.RouteId, StringComparison.Ordinal));
            return routes;
        }

        internal static List<ClusterConfig> BuildClusters(SFYarpBackendService service, Dictionary<string, Dictionary<string, DestinationConfig>> partitionDestinations, List<string> errors)
        {
            List<ClusterConfig> clusters = new List<ClusterConfig>();

            var metadata = ExtractMetadata("Yarp.Backend.Metadata.", service.FinalEffectiveLabels);

            // Populate service fabric info into metric dimensions.
            metadata["__SF.ApplicationTypeName"] = service.FabricApplication.Application.ApplicationTypeName;
            metadata["__SF.ApplicationName"] = service.FabricApplication.Application.ApplicationName.ToString();
            metadata["__SF.ServiceTypeName"] = service.FabricService.Service.ServiceTypeName;
            metadata["__SF.ServiceName"] = service.FabricService.Service.ServiceName.ToString();
            foreach (var entry in partitionDestinations)
            {
                string partitionId = entry.Key;
                var destinations = partitionDestinations.Count > 0 ? entry.Value : new Dictionary<string, DestinationConfig>();
                string clusterId = GetClusterId(service.FabricService.Service.ServiceName, service.FinalEffectiveLabels);

                var versionLabel = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HttpRequest.Version", null, errors);

                var sslProtocolsLabel = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.SslProtocols", null, errors);

#if NET
                var versionPolicyLabel = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HttpRequest.VersionPolicy", null, errors);
#endif

                var cluster = new ClusterConfig
                {
                    ClusterId = service.FabricService.Partitions.Count > 1 ? clusterId + "/" + partitionId : clusterId,
                    LoadBalancingPolicy = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.LoadBalancingPolicy", null, errors),
                    SessionAffinity = new SessionAffinityConfig
                    {
                        Enabled = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Enabled", null, errors),
                        Policy = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Policy", null, errors),
                        FailurePolicy = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.FailurePolicy", null, errors),
                        AffinityKeyName = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.AffinityKeyName", null, errors),
                        Cookie = new SessionAffinityCookieConfig
                        {
                            Path = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.Path", null, errors),
                            SameSite = GetLabel<SameSiteMode?>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.SameSite", null, errors),
                            HttpOnly = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.HttpOnly", null, errors),
                            MaxAge = GetTimeSpanLabel(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.MaxAge", null, errors),
                            Domain = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.Domain", null, errors),
                            IsEssential = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.IsEssential", null, errors),
                            SecurePolicy = GetLabel<CookieSecurePolicy?>(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.SecurePolicy", null, errors),
                            Expiration = GetTimeSpanLabel(service.FinalEffectiveLabels, "Yarp.Backend.SessionAffinity.Cookie.Expiration", null, errors),
                        },
                    },
                    HttpRequest = new ForwarderRequestConfig
                    {
                        ActivityTimeout = GetTimeSpanLabel(service.FinalEffectiveLabels, "Yarp.Backend.HttpRequest.ActivityTimeout", null, errors),
                        Version = !string.IsNullOrEmpty(versionLabel) ? Version.Parse(versionLabel + (versionLabel.Contains('.') ? string.Empty : ".0")) : null,
                        AllowResponseBuffering = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HttpRequest.AllowResponseBuffering", null, errors),
#if NET
                        VersionPolicy = !string.IsNullOrEmpty(versionLabel) ? Enum.Parse<HttpVersionPolicy>(versionPolicyLabel) : null,
#endif
                    },
                    HealthCheck = new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Active.Enabled", null, errors),
                            Interval = GetTimeSpanLabel(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Active.Interval", null, errors),
                            Timeout = GetTimeSpanLabel(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Active.Timeout", null, errors),
                            Path = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Active.Path", null, errors),
                            Policy = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Active.Policy", null, errors),
                        },
                        Passive = new PassiveHealthCheckConfig
                        {
                            Enabled = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Passive.Enabled", null, errors),
                            Policy = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Passive.Policy", null, errors),
                            ReactivationPeriod = GetTimeSpanLabel(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.Passive.ReactivationPeriod", null, errors),
                        },
                        AvailableDestinationsPolicy = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HealthCheck.AvailableDestinationsPolicy", null, errors),
                    },
                    HttpClient = new HttpClientConfig
                    {
                        DangerousAcceptAnyServerCertificate = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.DangerousAcceptAnyServerCertificate", null, errors),
                        MaxConnectionsPerServer = GetLabel<int?>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.MaxConnectionsPerServer", null, errors),
                        SslProtocols = !string.IsNullOrEmpty(sslProtocolsLabel) ? Enum.Parse<SslProtocols>(sslProtocolsLabel) : null,
#if NET
                        EnableMultipleHttp2Connections = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.EnableMultipleHttp2Connections", null, errors),
                        RequestHeaderEncoding = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.RequestHeaderEncoding", null, errors),
#endif
                        WebProxy = new WebProxyConfig
                        {
                            Address = GetLabel<Uri>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.WebProxy.Address", null, errors),
                            BypassOnLocal = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.WebProxy.BypassOnLocal", null, errors),
                            UseDefaultCredentials = GetLabel<bool?>(service.FinalEffectiveLabels, "Yarp.Backend.HttpClient.WebProxy.UseDefaultCredentials", null, errors),
                        },
                    },
                    Metadata = metadata,
                    Destinations = destinations,
                };

                if (errors.Count > 0)
                {
                    // Reading a label above may have failed due to bad user inputs.
                    // If so, abort and report the error.
                    return null;
                }
                clusters.Add(cluster);
            }
            return clusters;
        }

        private static string GetClusterId(Uri serviceName, IReadOnlyDictionary<string, string> labels)
        {
            if (!labels.TryGetValue("Yarp.Backend.BackendId", out string backendId) ||
                string.IsNullOrEmpty(backendId))
            {
                backendId = serviceName.ToString();
            }

            return backendId;
        }

        private static IReadOnlyList<string> SplitHosts(string hosts)
        {
            return hosts?.Split(',').Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        }

        private static IReadOnlyList<string> SplitMethods(string hosts)
        {
            return hosts?.Split(',').Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        }

        private static bool ContainsKey(string expectedKeyName, ReadOnlySpan<char> actualKey, out ReadOnlySpan<char> keyRemainder)
        {
            keyRemainder = default;

            if (!actualKey.StartsWith(expectedKeyName, StringComparison.Ordinal))
            {
                return false;
            }

            keyRemainder = actualKey.Slice(expectedKeyName.Length);
            return true;
        }

        private static Dictionary<string, string> ExtractMetadata(string metadataPrefix, IReadOnlyDictionary<string, string> labels)
        {
            var clusterMetadata = new Dictionary<string, string>();
            foreach (var kvp in labels)
            {
                if (kvp.Key.StartsWith(metadataPrefix, StringComparison.Ordinal))
                {
                    clusterMetadata[kvp.Key.Substring(metadataPrefix.Length)] = kvp.Value;
                }
            }

            return clusterMetadata;
        }

        private static TValue GetLabel<TValue>(IReadOnlyDictionary<string, string> labels, string key, TValue defaultValue, List<string> errors)
        {
            if (labels.TryGetValue(key, out string value))
            {
                try
                {
                    return (TValue)TypeDescriptor.GetConverter(typeof(TValue)).ConvertFromString(value);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
                {
                    errors.Add($"Could not convert label {key}='{value}' to type {typeof(TValue).FullName}.");
                }
            }

            return defaultValue;
        }

        private static TimeSpan? GetTimeSpanLabel(IReadOnlyDictionary<string, string> labels, string key, TimeSpan? defaultValue, List<string> errors)
        {
            if (!labels.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            // Format "c" => [-][d'.']hh':'mm':'ss['.'fffffff].
            // You also can find more info at https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings#the-constant-c-format-specifier
            if (!TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out var result))
            {
                // throw new ConfigException($"Could not convert label {key}='{value}' to type TimeSpan. Use the format '[d.]hh:mm:ss'.");
                errors.Add($"Could not convert label {key}='{value}' to type TimeSpan. Use the format '[d.]hh:mm:ss'.");
            }
            return result;
        }

        private static TValue ConvertLabelValue<TValue>(string key, string value, List<string> errors)
        {
            try
            {
                return (TValue)TypeDescriptor.GetConverter(typeof(TValue)).ConvertFromString(value);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
            {
                errors.Add($"Could not convert label {key}='{value}' to type {typeof(TValue).FullName}.");
                return default(TValue);
            }
        }
        private class RouteHeaderFields
        {
            public string Name { get; internal set; }
            public IReadOnlyList<string> Values { get; internal set; }
            public bool IsCaseSensitive { get; internal set; }
            public HeaderMatchMode Mode { get; internal set; }
        }
        private class RouteQueryParameterFields
        {
            public string Name { get; internal set; }
            public IReadOnlyList<string> Values { get; internal set; }
            public bool IsCaseSensitive { get; internal set; }
            public QueryParameterMatchMode Mode { get; internal set; }
        }
    }
}