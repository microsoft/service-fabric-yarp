// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using IslandGateway.Common.Util;
using IslandGateway.ServiceFabricIntegration;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;

namespace IslandGateway.FabricDiscovery.Util
{
    /// <summary>
    /// Helper class to parse configuration labels of the gateway into actual objects.
    /// </summary>
    internal static class LabelsParserV2
    {
        private static readonly Regex AllowedRouteNamesRegex = new Regex("^[a-zA-Z0-9_-]+$");

        internal static bool IsEnabled(IReadOnlyDictionary<string, string> labels)
        {
            return string.Equals(labels.GetValueOrDefault("IslandGateway.Enable", null), "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool UseDynamicOverrides(IReadOnlyDictionary<string, string> labels)
        {
            return string.Equals(labels.GetValueOrDefault("IslandGateway.EnableDynamicOverrides", null), "true", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<RouteConfig> BuildRoutes(Uri serviceName, IReadOnlyDictionary<string, string> labels, List<string> errors)
        {
            // Look for route IDs
            const string RoutesLabelsPrefix = "IslandGateway.Routes.";
            var routesNames = new HashSet<string>();
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

                if (!labels.TryGetValue($"{thisRoutePrefix}.Path", out string path))
                {
                    errors.Add($"Missing {thisRoutePrefix}.Path.");
                    return null;
                }

                labels.TryGetValue($"{thisRoutePrefix}.CorsPolicy", out string corsPolicy);
                var route = new RouteConfig
                {
                    RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(routeName)}",

                    Match = new RouteMatch
                    {
                        // TODO: Add support for other matchers like Host, Headers, ...
                        Path = path,
                    },

                    Order = GetLabel(labels, $"{thisRoutePrefix}.Order", (int?)null, errors),
                    ClusterId = backendId,
                    Metadata = ExtractMetadata($"{thisRoutePrefix}.Metadata.", labels),
                    CorsPolicy = corsPolicy,
                };

                if (errors.Count > 0)
                {
                    // Reading a label above may have failed due to bad user inputs.
                    // If so, abort and report the error.
                    return null;
                }

                routes.Add(route);
            }

            // Sort discovered routes so that final output is deterministic.
            // Note that we shouldn't need this to achieve E2E determinism, but it helps in case there are bugs elsewhere.
            routes.Sort((a, b) => string.Compare(a.RouteId, b.RouteId, StringComparison.Ordinal));
            return routes;
        }

        internal static List<ClusterConfig> BuildClustersWithoutDestinations(ApplicationWrapper application, ServiceWrapper service, IReadOnlyDictionary<string, string> labels, List<string> errors)
        {
            string clusterId = GetClusterId(service.ServiceName, labels);

            var metadata = ExtractMetadata("IslandGateway.Backend.Metadata.", labels);

            // Populate service fabric info into metric dimensions.
            metadata["__SF.ApplicationTypeName"] = application.ApplicationTypeName;
            metadata["__SF.ApplicationName"] = application.ApplicationName.ToString();
            metadata["__SF.ServiceTypeName"] = service.ServiceTypeName;
            metadata["__SF.ServiceName"] = service.ServiceName.ToString();

            var cluster = new ClusterConfig
            {
                ClusterId = clusterId,
                LoadBalancingPolicy = LoadBalancingPolicies.Random,
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = GetLabel(labels, "IslandGateway.Backend.Healthcheck.Enabled", false, errors),
                        Interval = GetLabel<TimeSpanIso8601>(labels, "IslandGateway.Backend.Healthcheck.Interval", TimeSpan.Zero, errors),
                        Timeout = GetLabel<TimeSpanIso8601>(labels, "IslandGateway.Backend.Healthcheck.Timeout", TimeSpan.Zero, errors),
                        Path = GetLabel<string>(labels, "IslandGateway.Backend.Healthcheck.Path", null, errors),
                    },
                },
                Metadata = metadata,
                HttpRequest = new ForwarderRequestConfig
                {
                    Timeout = GetLabel<TimeSpanIso8601>(labels, "IslandGateway.Backend.ProxyTimeout", TimeSpan.FromSeconds(30), errors),
                },
                Destinations = new Dictionary<string, DestinationConfig>(),
            };

            if (errors.Count > 0)
            {
                // Reading a label above may have failed due to bad user inputs.
                // If so, abort and report the error.
                return null;
            }

            return new List<ClusterConfig> { cluster };
        }

        private static string GetClusterId(Uri serviceName, IReadOnlyDictionary<string, string> labels)
        {
            if (!labels.TryGetValue("IslandGateway.Backend.BackendId", out string backendId) ||
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