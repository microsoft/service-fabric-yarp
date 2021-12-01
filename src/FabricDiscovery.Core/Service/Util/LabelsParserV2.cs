// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Authentication;
using System.Text.RegularExpressions;
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
        private static readonly Regex AllowedRouteNamesRegex = new Regex("^[a-zA-Z0-9_-]+$");

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
            // Look for route IDs
            const string RoutesLabelsPrefix = "Yarp.Routes.";
            var routesNames = new HashSet<string>();
            IReadOnlyDictionary<string, string> labels = backendService.FinalEffectiveLabels;
            Uri serviceName = backendService.FabricService.Service.ServiceName;

            foreach (var kvp in labels)
            {
                if (kvp.Key.Length > RoutesLabelsPrefix.Length && kvp.Key.StartsWith(RoutesLabelsPrefix, StringComparison.Ordinal))
                {
                    string suffix = kvp.Key.Substring(RoutesLabelsPrefix.Length);
                    int routeNameLength = suffix.IndexOf('.');
                    if (routeNameLength == -1)
                    {
                        // No route name encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                        continue;
                    }

                    var routeName = suffix.Substring(0, routeNameLength);
                    if (!AllowedRouteNamesRegex.IsMatch(routeName))
                    {
                        errors.Add($"Invalid route name '{routeName}', should only contain alphanumerical characters, underscores or hyphens.");
                        return null;
                    }

                    routesNames.Add(routeName);
                }
            }

            // Build the routes
            var routes = new List<RouteConfig>();
            string backendId = GetClusterId(serviceName, labels);
            foreach (var routeName in routesNames)
            {
                string thisRoutePrefix = $"{RoutesLabelsPrefix}{routeName}";

                labels.TryGetValue($"{thisRoutePrefix}.Path", out string path);

                // Combine default path generation (/appName/serviceName) and user defined path from service manifest.
                var finalPath = serviceName.AbsolutePath + path;

                labels.TryGetValue($"{thisRoutePrefix}.CorsPolicy", out string corsPolicy);

                if (errors.Count > 0)
                {
                    // Reading a label above may have failed due to bad user inputs.
                    // If so, abort and report the error.
                    return null;
                }

                // Stateful/stateless service with multiple partitions
                if (backendService.FabricService.Partitions.Count > 1)
                {
                    foreach (var partition in backendService.FabricService.Partitions)
                    {
                        RouteQueryParameter routeQueryParameter = new RouteQueryParameter
                        {
                            Name = "PartitionID",
                            Values = new List<string> { $"{partition.Partition.PartitionId}" },
                        };

                        var route = new RouteConfig
                        {
                            RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(partition.Partition.PartitionId.ToString())}:{Uri.EscapeDataString(routeName)}",

                            Match = new RouteMatch
                            {
                                // TODO: Add support for other matchers like Host, Headers, ...
                                Path = finalPath,
                                QueryParameters = new List<RouteQueryParameter> { routeQueryParameter },
                            },

                            Order = GetLabel(labels, $"{thisRoutePrefix}.Order", (int?)null, errors),
                            ClusterId = backendId + "/" + partition.Partition.PartitionId,
                            Metadata = ExtractMetadata($"{thisRoutePrefix}.Metadata.", labels),
                            CorsPolicy = corsPolicy,
                            Transforms = new[]
                            {
                                new Dictionary<string, string> { { "PathRemovePrefix", serviceName.AbsolutePath } }, new Dictionary<string, string> { { "QueryRemoveParameter", "PartitionID" } },
                            },
                        };
                        routes.Add(route);
                    }
                }
                else
                { // Stateless/stateful service with singleton partition
                    var route = new RouteConfig
                    {
                        RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(routeName)}",

                        Match = new RouteMatch
                        {
                            // TODO: Add support for other matchers like Host, Headers, ...
                            Path = finalPath,
                        },

                        Order = GetLabel(labels, $"{thisRoutePrefix}.Order", (int?)null, errors),
                        ClusterId = backendId,
                        Metadata = ExtractMetadata($"{thisRoutePrefix}.Metadata.", labels),
                        CorsPolicy = corsPolicy,
                        Transforms = new[]
                        {
                            new Dictionary<string, string> { { "PathRemovePrefix", serviceName.AbsolutePath } },
                        },
                    };
                    routes.Add(route);
                }
            }

            // Sort discovered routes so that final output is deterministic.
            // Note that we shouldn't need this to achieve E2E determinism, but it helps in case there are bugs elsewhere.
            routes.Sort((a, b) => string.Compare(a.RouteId, b.RouteId, StringComparison.Ordinal));
            return routes;
        }

        internal static List<ClusterConfig> BuildClustersWithDestinations(SFYarpBackendService service, Dictionary<string, Dictionary<string, DestinationConfig>> partitionDestinations, List<string> errors)
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
                string partitionID = entry.Key;
                var destinations = entry.Value;
                string clusterId = GetClusterId(service.FabricService.Service.ServiceName, service.FinalEffectiveLabels);

                var cluster = new ClusterConfig
                {
                    ClusterId = service.FabricService.Partitions.Count > 1 ? clusterId + "/" + partitionID : clusterId,
                    LoadBalancingPolicy = LoadBalancingPolicies.Random,
                    HealthCheck = new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = GetLabel(service.FinalEffectiveLabels, "Yarp.Backend.Healthcheck.Enabled", false, errors),
                            Interval = GetLabel<TimeSpanIso8601>(service.FinalEffectiveLabels, "Yarp.Backend.Healthcheck.Interval", TimeSpan.Zero, errors),
                            Timeout = GetLabel<TimeSpanIso8601>(service.FinalEffectiveLabels, "Yarp.Backend.Healthcheck.Timeout", TimeSpan.Zero, errors),
                            Path = GetLabel<string>(service.FinalEffectiveLabels, "Yarp.Backend.Healthcheck.Path", null, errors),
                        },
                    },
                    Metadata = metadata,
                    HttpClient = new HttpClientConfig { DangerousAcceptAnyServerCertificate = true },
                    HttpRequest = new ForwarderRequestConfig
                    {
                        ActivityTimeout = GetLabel<TimeSpanIso8601>(service.FinalEffectiveLabels, "Yarp.Backend.ProxyTimeout", TimeSpan.FromSeconds(30), errors),
                    },
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
    }
}