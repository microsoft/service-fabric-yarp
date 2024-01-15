// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Description;
using System.Fabric.Health;
using System.Fabric.Query;
using System.Linq;
using System.Security.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tests.Common;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.SessionAffinity;
using Yarp.ServiceFabric.Common.Abstractions.Telemetry;
using Yarp.ServiceFabric.Common.Telemetry;
using Yarp.ServiceFabric.FabricDiscovery.SFYarpConfig;
using Yarp.ServiceFabric.FabricDiscovery.Topology;
using Yarp.ServiceFabric.ServiceFabricIntegration;

namespace Yarp.ServiceFabric.FabricDiscovery.Util.Tests
{
    public class LabelsParserTests
    {
#pragma warning disable SA1309 // Field names should not begin with underscore
        // private static readonly Uri _testServiceName = new Uri("fabric:/App1/Svc1");
        private static readonly Uri _testServiceName = new Uri("fabric:/CoreServices.SFYarp/EchoService");
        private static readonly string _testAppName = "CoreServices.SFYarp";
        private static readonly string _testSvcName = "EchoService";
        private static readonly List<string> _partitionIds = new List<string> { "8c034a68-beba-4527-a206-323c831c61fb" };
        private static readonly List<string> _replicaIds = new List<string> { "132587450787126998", "132587449112860863" };

        // Uncomment to create real null Ilogger<T>
        // private static readonly ILogger<SFYarpConfigProducer> _logger = new NullLogger<SFYarpConfigProducer>();

        // Create real Ilogger<T> and log to console;
        private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(loggingBuilder => loggingBuilder.SetMinimumLevel(LogLevel.Trace).AddConsole());
        private static readonly ILogger<SFYarpConfigProducer> _logger = _loggerFactory.CreateLogger<SFYarpConfigProducer>();
        private static readonly IOptions<FabricDiscoveryOptions> _options = Options.Create(new FabricDiscoveryOptions());
        private static readonly IOperationLogger _operationLogger = new NullOperationLogger();
        private static readonly SFYarpConfigProducer SFYarpConfigProducer = new SFYarpConfigProducer(_options, _logger, _operationLogger);

#pragma warning restore SA1309 // Field names should not begin with underscore

        [Fact]
        public void BuildCluster_CompleteLabels_Works()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Enable", "true" },
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Backend.LoadBalancingPolicy", "LeastRequests" },
                { "Yarp.Backend.SessionAffinity.Enabled", "true" },
                { "Yarp.Backend.SessionAffinity.Policy", "Cookie" },
                { "Yarp.Backend.SessionAffinity.FailurePolicy", "Return503Error" },
                { "Yarp.Backend.SessionAffinity.AffinityKeyName", "Key1" },
                { "Yarp.Backend.SessionAffinity.Cookie.Domain", "localhost" },
                { "Yarp.Backend.SessionAffinity.Cookie.Expiration", "03:00:00" },
                { "Yarp.Backend.SessionAffinity.Cookie.HttpOnly", "true" },
                { "Yarp.Backend.SessionAffinity.Cookie.IsEssential", "true" },
                { "Yarp.Backend.SessionAffinity.Cookie.MaxAge", "1.00:00:00" },
                { "Yarp.Backend.SessionAffinity.Cookie.Path", "mypath" },
                { "Yarp.Backend.SessionAffinity.Cookie.SameSite", "Strict" },
                { "Yarp.Backend.SessionAffinity.Cookie.SecurePolicy", "SameAsRequest" },
                { "Yarp.Backend.HttpRequest.ActivityTimeout", "00:00:17" },
                { "Yarp.Backend.HttpRequest.AllowResponseBuffering", "true" },
                { "Yarp.Backend.HttpRequest.Version", "1.1" },
    #if NET
                { "Yarp.Backend.HttpRequest.VersionPolicy", "RequestVersionExact" },
    #endif
                { "Yarp.Backend.HealthCheck.Active.Enabled", "true" },
                { "Yarp.Backend.HealthCheck.Active.Interval", "00:00:05" },
                { "Yarp.Backend.HealthCheck.Active.Timeout", "00:00:06" },
                { "Yarp.Backend.HealthCheck.Active.Policy", "MyActiveHealthPolicy" },
                { "Yarp.Backend.HealthCheck.Active.Path", "/api/health" },
                { "Yarp.Backend.HealthCheck.Passive.Enabled", "true" },
                { "Yarp.Backend.HealthCheck.Passive.Policy", "MyPassiveHealthPolicy" },
                { "Yarp.Backend.HealthCheck.Passive.ReactivationPeriod", "00:00:07" },
                { "Yarp.Backend.Metadata.Foo", "Bar" },
                { "Yarp.Backend.HttpClient.DangerousAcceptAnyServerCertificate", "true" },
                { "Yarp.Backend.HttpClient.MaxConnectionsPerServer", "1000" },
                { "Yarp.Backend.HttpClient.SslProtocols", "Tls12" },
    #if NET
                { "Yarp.Backend.HttpClient.EnableMultipleHttp2Connections", "false" },
                { "Yarp.Backend.HttpClient.RequestHeaderEncoding", "utf-8" },
    #endif
                { "Yarp.Backend.HttpClient.WebProxy.Address", "https://10.20.30.40" },
                { "Yarp.Backend.HttpClient.WebProxy.BypassOnLocal", "true" },
                { "Yarp.Backend.HttpClient.WebProxy.UseDefaultCredentials", "false" },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            ClusterConfig clusterConfig = new ClusterConfig
            {
                ClusterId = "MyCoolClusterId",
                LoadBalancingPolicy = LoadBalancingPolicies.LeastRequests,
                SessionAffinity = new SessionAffinityConfig
                {
                    Enabled = true,
                    Policy = SessionAffinityConstants.Policies.Cookie,
                    FailurePolicy = SessionAffinityConstants.FailurePolicies.Return503Error,
                    AffinityKeyName = "Key1",
                    Cookie = new SessionAffinityCookieConfig
                    {
                        Domain = "localhost",
                        Expiration = TimeSpan.FromHours(3),
                        HttpOnly = true,
                        IsEssential = true,
                        MaxAge = TimeSpan.FromDays(1),
                        Path = "mypath",
                        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                        SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest,
                    },
                },
                HttpRequest = new ForwarderRequestConfig
                {
                    ActivityTimeout = TimeSpan.FromSeconds(17),
                    Version = new Version(1, 1),
                    AllowResponseBuffering = true,
#if NET
                    VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
#endif
                },
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = true,
                        Interval = TimeSpan.FromSeconds(5),
                        Timeout = TimeSpan.FromSeconds(6),
                        Path = "/api/health",
                        Policy = "MyActiveHealthPolicy",
                    },
                    Passive = new PassiveHealthCheckConfig
                    {
                        Enabled = true,
                        Policy = "MyPassiveHealthPolicy",
                        ReactivationPeriod = TimeSpan.FromSeconds(7),
                    },
                },
                Metadata = new Dictionary<string, string>
                {
                    { "Foo", "Bar" },
                    { "__SF.ApplicationTypeName",  "CoreServices.SFYarpAppType" },
                    { "__SF.ApplicationName", "fabric:/CoreServices.SFYarp" },
                    { "__SF.ServiceTypeName", "SFYarpEchoType" },
                    { "__SF.ServiceName", "fabric:/CoreServices.SFYarp/EchoService" },
                },
                HttpClient = new HttpClientConfig
                {
                    DangerousAcceptAnyServerCertificate = true,
#if NET
                    EnableMultipleHttp2Connections = false,
                    RequestHeaderEncoding = "utf-8",
#endif
                    MaxConnectionsPerServer = 1000,
                    SslProtocols = SslProtocols.Tls12,
                    WebProxy = new WebProxyConfig
                    {
                        Address = new Uri("https://10.20.30.40"),
                        BypassOnLocal = true,
                        UseDefaultCredentials = false,
                    },
                },

                Destinations = this.SimulateSFYarpDestinations(_partitionIds, _replicaIds),
            };
            var expectedCluster = new List<ClusterConfig> { clusterConfig };

            cluster.Should().BeEquivalentTo(expectedCluster);
        }

        [Fact]
        public void BuildCluster_IncompleteLabels_UsesDefaultValues()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Backend.SessionAffinity.AffinityKeyName", "Key1" },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            var expectedCluster = new ClusterConfig
            {
                ClusterId = "MyCoolClusterId",
                SessionAffinity = new SessionAffinityConfig
                {
                    AffinityKeyName = "Key1",
                    Cookie = new SessionAffinityCookieConfig(),
                },
                HttpRequest = new ForwarderRequestConfig(),
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig(),
                    Passive = new PassiveHealthCheckConfig(),
                },
                Metadata = new Dictionary<string, string>
                {
                    { "__SF.ApplicationTypeName",  "CoreServices.SFYarpAppType" },
                    { "__SF.ApplicationName", "fabric:/CoreServices.SFYarp" },
                    { "__SF.ServiceTypeName", "SFYarpEchoType" },
                    { "__SF.ServiceName", "fabric:/CoreServices.SFYarp/EchoService" },
                },
                HttpClient = new HttpClientConfig
                {
                    WebProxy = new WebProxyConfig
                    {
                    },
                },
                Destinations = this.SimulateSFYarpDestinations(_partitionIds, _replicaIds),
            };
            cluster.Should().BeEquivalentTo(new List<ClusterConfig> { expectedCluster });
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("True", true)]
        [InlineData("TRUE", true)]
        [InlineData("false", false)]
        [InlineData("False", false)]
        [InlineData("FALSE", false)]
        [InlineData(null, null)]
        [InlineData("", null)]
        public void BuildCluster_HealthCheckOptions_Enabled_Valid(string label, bool? expected)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Backend.HealthCheck.Active.Enabled", label },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            cluster[0].HealthCheck.Active.Enabled.Should().Be(expected);
        }

        [Theory]
        [InlineData("notbool")]
        [InlineData(" ")]
        public void BuildCluster_HealthCheckOptions_Enabled_Invalid(string label)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Backend.HealthCheck.Active.Enabled", label },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Could not convert label Yarp.Backend.HealthCheck.Active.Enabled='{label}' *");
        }

        [Fact]
        public void BuildCluster_MissingBackendId_UsesServiceName()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.Quota.Burst", "2.3" },
                { "Yarp.Backend.Partitioning.Count", "5" },
                { "Yarp.Backend.Partitioning.KeyExtractor", "Header('x-ms-organization-id')" },
                { "Yarp.Backend.Partitioning.Algorithm", "SHA256" },
                { "Yarp.Backend.HealthCheck.Active.Interval", "00:00:5" },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            cluster[0].ClusterId.Should().Be(_testServiceName.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void BuildCluster_NullTimespan(string value)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.HealthCheck.Active.Interval", value },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            cluster[0].HealthCheck.Active.Interval.Should().BeNull();
        }

        [Theory]
        [InlineData("Yarp.Backend.HealthCheck.Active.Interval", "1S")]
        [InlineData("Yarp.Backend.HealthCheck.Active.Timeout", "foobar")]
        public void BuildCluster_InvalidValues_Throws(string key, string invalidValue)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { key, invalidValue },
            };

            List<string> errors = new List<string>();
            var cluster = this.SimulateBuildingSFYarpClusters(labels, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Could not convert label {key}='{invalidValue}' *");
        }

        [Fact]
        public void BuildRoutes_SingleRoute_Works()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Methods", "GET,  PUT" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.MatchQueries.[0].Mode", "Exact" },
                { "Yarp.Routes.MyRoute.MatchQueries.[0].Name", "orgID" },
                { "Yarp.Routes.MyRoute.MatchQueries.[0].Values", "123456789" },
                { "Yarp.Routes.MyRoute.MatchQueries.[0].IsCaseSensitive", "true" },
                { "Yarp.Routes.MyRoute.MatchQueries.[1].Mode", "Exact" },
                { "Yarp.Routes.MyRoute.MatchQueries.[1].Name", "customerID" },
                { "Yarp.Routes.MyRoute.MatchQueries.[1].Values", "123456789" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[0].Mode", "ExactHeader" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[0].Name", "x-company-key" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[0].Values", "contoso" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[0].IsCaseSensitive", "true" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[1].Mode", "ExactHeader" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[1].Name", "x-environment" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[1].Values", "dev, uat" },
                { "Yarp.Routes.MyRoute.Transforms.[0].ResponseHeader", "X-Foo" },
                { "Yarp.Routes.MyRoute.Transforms.[0].Append", "Bar" },
                { "Yarp.Routes.MyRoute.Transforms.[0].When", "Always" },
                { "Yarp.Routes.MyRoute.Transforms.[1].ResponseHeader", "X-Ping" },
                { "Yarp.Routes.MyRoute.Transforms.[1].Append", "Pong" },
                { "Yarp.Routes.MyRoute.Transforms.[1].When", "Success" },
                { "Yarp.Routes.MyRoute.AuthorizationPolicy", "Policy1" },
                { "Yarp.Routes.MyRoute.CorsPolicy", "Policy1" },
            };

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:MyRoute",
                    Match = new RouteMatch
                    {
                        Hosts = new[] { "example.com" },
                        Methods = new[] { "GET", "PUT" },
                        Path = _testServiceName.AbsolutePath,
                        QueryParameters = new List<RouteQueryParameter>
                        {
                            new RouteQueryParameter()
                            {
                                Mode = QueryParameterMatchMode.Exact,
                                Name = "orgID",
                                Values = new string[] { "123456789" },
                                IsCaseSensitive = true,
                            },
                            new RouteQueryParameter()
                            {
                                Mode = QueryParameterMatchMode.Exact,
                                Name = "customerID",
                                Values = new string[] { "123456789" },
                                IsCaseSensitive = false,
                            },
                        },
                        Headers = new List<RouteHeader>
                        {
                            new RouteHeader()
                            {
                                Mode = HeaderMatchMode.ExactHeader,
                                Name = "x-company-key",
                                Values = new string[] { "contoso" },
                                IsCaseSensitive = true,
                            },
                            new RouteHeader()
                            {
                                Mode = HeaderMatchMode.ExactHeader,
                                Name = "x-environment",
                                Values = new string[] { "dev", "uat" },
                                IsCaseSensitive = false,
                            },
                        },
                    },
                    Order = 2,
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>
                    {
                        { "Foo", "Bar" },
                    },
                    Transforms = new List<Dictionary<string, string>>
                    {
                        new Dictionary<string, string>
                        {
                            { "ResponseHeader", "X-Foo" },
                            { "Append", "Bar" },
                            { "When", "Always" },
                        },
                        new Dictionary<string, string>
                        {
                            { "ResponseHeader", "X-Ping" },
                            { "Append", "Pong" },
                            { "When", "Success" },
                        },
                        new Dictionary<string, string>
                        {
                            { "PathRemovePrefix", _testServiceName.AbsolutePath },
                        },
                    },
                    AuthorizationPolicy = "Policy1",
                    CorsPolicy = "Policy1",
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Fact]
        public void BuildRoutes_IncompleteRoute_UsesDefaults()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
            };

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:MyRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example.com" },
                    },
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        /// <summary>
        /// The LabelParser is not expected to invoke route parsing logic, and should treat the objects as plain data containers.
        /// </summary>
        [Fact]
        public void BuildRoutes_SingleRouteWithSemanticallyInvalidRule_WorksAndDoesNotThrow()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "'this invalid thing" },
            };

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:MyRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "'this invalid thing" },
                    },
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void BuildRoutes_MissingBackendId_UsesServiceName(int scenario)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
            };

            if (scenario == 1)
            {
                labels.Add("Yarp.Backend.BackendId", string.Empty);
            }

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = $"{Uri.EscapeDataString(_testServiceName.ToString())}:MyRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example.com" },
                    },
                    Order = 2,
                    ClusterId = _testServiceName.ToString(),
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Fact]
        public void BuildRoutes_MissingHost_Works()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Routes.MyRoute.Path", "/{**catchall}" },
            };

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = $"{Uri.EscapeDataString(_testServiceName.ToString())}:MyRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath + "/{**catchall}",
                    },
                    ClusterId = _testServiceName.ToString(),
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Fact]
        public void BuildRoutes_InvalidOrder_Throws()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "this is no number" },
            };

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Could not convert label Yarp.Routes.MyRoute.Order='this is no number' *");
        }

        [Theory]
        [InlineData("justcharacters")]
        [InlineData("UppercaseCharacters")]
        [InlineData("numbers1234")]
        [InlineData("Under_Score")]
        [InlineData("Hyphen-Hyphen")]
        public void BuildRoutes_ValidRouteName_Works(string routeName)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { $"Yarp.Routes.{routeName}.Hosts", "example.com" },
                { $"Yarp.Routes.{routeName}.Order", "2" },
            };

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = $"MyCoolClusterId:{routeName}",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example.com" },
                    },
                    Order = 2,
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Theory]
        [InlineData("Yarp.Routes..Order", "that was an empty route name")]
        [InlineData("Yarp.Routes..Hosts", "that was an empty route name")]
        [InlineData("Yarp.Routes.  .Hosts", "that was an empty route name")]
        [InlineData("Yarp.Routes..", "that was an empty route name")]
        [InlineData("Yarp.Routes...", "that was an empty route name")]
        [InlineData("Yarp.Routes.FunnyChars!.Hosts", "some value")]
        [InlineData("Yarp.Routes.'FunnyChars'.Order", "some value")]
        [InlineData("Yarp.Routes.FunnyChárs.Metadata", "some value")]
        [InlineData("Yarp.Routes.Funny+Chars.Hosts", "some value")]
        public void BuildRoutes_InvalidRouteName_Throws(string invalidKey, string value)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Invalid route name '*', should only contain alphanumerical characters, underscores or hyphens.");
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute.Transforms. .ResponseHeader", "Blank transform index")]
        [InlineData("Yarp.Routes.MyRoute.Transforms.string.ResponseHeader", "string header name not accepted.. just [num]")]
        [InlineData("Yarp.Routes.MyRoute.Transforms.1.Response", "needs square brackets")]
        public void BuildRoutes_InvalidTransformIndex_Throws(string invalidKey, string value)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Invalid transform index '*', should be transform index wrapped in square brackets.");
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute.MatchQueries. .Name", "orgID")]
        [InlineData("Yarp.Routes.MyRoute.MatchQueries.string.Name", "orgID")]
        [InlineData("Yarp.Routes.MyRoute.MatchQueries.1.Name", "orgID")]
        public void BuildRoutes_InvalidQueryMatchIndex_Throws(string invalidKey, string value)
        {
            // Arrange
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Invalid query matching index '*', should be query index wrapped in square brackets.");
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute.MatchQueries.[0].UnknownProperty", "some value")]
        public void BuildRoutes_InvalidQueryMatchProperty_Throws(string invalidKey, string value)
        {
            // Arrange
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Invalid query matching property '*', only valid values are Name, Values, IsCaseSensitive and Mode.");
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute0.MatchQueries.[0].Values", "apples, oranges, grapes", new string[] { "apples", "oranges", "grapes" })]
        [InlineData("Yarp.Routes.MyRoute0.MatchQueries.[0].Values", "apples,,oranges,grapes", new string[] { "apples", "", "oranges", "grapes" })]
        public void BuildRoutes_MatchQueryWithCSVs_Works(string invalidKey, string value, string[] expected)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute0.Hosts", "example0.com" },
                { "Yarp.Routes.MyRoute0.Metadata.Foo", "bar" },
                { "Yarp.Routes.MyRoute0.MatchQueries.[0].Name", "orgID" },
                { "Yarp.Routes.MyRoute0.MatchQueries.[0].Mode", "Exact" },
            };
            labels[invalidKey] = value;

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = $"MyCoolClusterId:MyRoute0",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example0.com" },
                        QueryParameters = new List<RouteQueryParameter>()
                        {
                            new RouteQueryParameter()
                            {
                                Name = "orgID",
                                Mode = QueryParameterMatchMode.Exact,
                                Values = expected,
                            },
                        },
                    },
                    Metadata = new Dictionary<string, string>()
                    {
                        { "Foo", "bar" },
                    },
                    ClusterId = "MyCoolClusterId",
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute.MatchHeaders. .Name", "x-header-name")]
        [InlineData("Yarp.Routes.MyRoute.MatchHeaders.string.Name", "x-header-name")]
        [InlineData("Yarp.Routes.MyRoute.MatchHeaders.1.Name", "x-header-name")]
        public void BuildRoutes_InvalidHeaderMatchIndex_Throws(string invalidKey, string value)
        {
            // Arrange
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Invalid header matching index '*', should be header index wrapped in square brackets.");
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute.MatchHeaders.[0].UnknownProperty", "some value")]
        public void BuildRoutes_InvalidHeaderMatchProperty_Throws(string invalidKey, string value)
        {
            // Arrange
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            errors.Should().HaveCount(1).And.ContainMatch($"Invalid header matching property '*', only valid values are Name, Values, IsCaseSensitive and Mode.");
        }

        [Theory]
        [InlineData("Yarp.Routes.MyRoute0.MatchHeaders.[0].Values", "apples, oranges, grapes", new string[] { "apples", "oranges", "grapes" })]
        [InlineData("Yarp.Routes.MyRoute0.MatchHeaders.[0].Values", "apples,,oranges,grapes", new string[] { "apples", "", "oranges", "grapes" })]
        public void BuildRoutes_MatchHeadersWithCSVs_Works(string invalidKey, string value, string[] expected)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute0.Hosts", "example0.com" },
                { "Yarp.Routes.MyRoute0.Metadata.Foo", "bar" },
                { "Yarp.Routes.MyRoute0.MatchHeaders.[0].Name", "x-test-header" },
                { "Yarp.Routes.MyRoute0.MatchHeaders.[0].Mode", "ExactHeader" },
            };
            labels[invalidKey] = value;

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = $"MyCoolClusterId:MyRoute0",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example0.com" },
                        Headers = new List<RouteHeader>()
                        {
                            new RouteHeader() { Name = "x-test-header", Mode = HeaderMatchMode.ExactHeader, Values = expected },
                        },
                    },
                    Metadata = new Dictionary<string, string>()
                    {
                        { "Foo", "bar" },
                    },
                    ClusterId = "MyCoolClusterId",
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("NotEven.TheNamespace", "some value")]
        [InlineData("Yarp.", "some value")]
        [InlineData("Routes.", "some value")]
        [InlineData("Yarp.Routes.", "some value")]
        [InlineData("Yarp.Routes.MyRoute.MatchQueries", "some value")]
        [InlineData("Yarp.Routes.MyRoute.MatchQueries.", "some value")]
        [InlineData("Yarp.Routes.MyRoute...MatchQueries", "some value")]
        [InlineData("Yarp.Routes.MyRoute.MatchHeaders", "some value")]
        [InlineData("Yarp.Routes.MyRoute.MatchHeaders.", "some value")]
        [InlineData("Yarp.Routes.MyRoute...MatchHeaders", "some value")]
        [InlineData("Yarp.Routes.MyRoute.Transforms", "some value")]
        [InlineData("Yarp.Routes.MyRoute.Transforms.", "some value")]
        [InlineData("Yarp.Routes.MyRoute...Transforms", "some value")]
        [InlineData("Yarp.Routes.MyRoute.Transform.", "some value")]
        [InlineData("Yarp.Routes", "some value")]
        [InlineData("Yarp..Routes.", "some value")]
        [InlineData("Yarp.....Routes.", "some value")]
        public void BuildRoutes_InvalidLabelKeys_IgnoresAndDoesNotThrow(string invalidKey, string value)
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Order", "2" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
            };
            labels[invalidKey] = value;

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:MyRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example.com" },
                    },
                    Order = 2,
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>
                    {
                        { "Foo", "Bar" },
                    },
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Fact]
        public void BuildRoutes_MultipleRoutes_Works()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Routes.MyRoute.Hosts", "example.com" },
                { "Yarp.Routes.MyRoute.Path", "v2/{**rest}" },
                { "Yarp.Routes.MyRoute.Order", "1" },
                { "Yarp.Routes.MyRoute.Metadata.Foo", "Bar" },
                { "Yarp.Routes.CoolRoute.Hosts", "example.net" },
                { "Yarp.Routes.CoolRoute.Order", "2" },
                { "Yarp.Routes.EvenCoolerRoute.Hosts", "example.org" },
                { "Yarp.Routes.EvenCoolerRoute.Order", "3" },
            };

            // var routes = LabelsParserV2.BuildRoutes(_testServiceName, labels);
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            var expectedRoutes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:MyRoute",
                    Match = new RouteMatch
                    {
                        Hosts = new[] { "example.com" },
                        Path = _testServiceName.AbsolutePath + "v2/{**rest}",
                    },
                    Order = 1,
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string> { { "Foo", "Bar" } },
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:CoolRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example.net" },
                    },
                    Order = 2,
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
                new RouteConfig
                {
                    RouteId = "MyCoolClusterId:EvenCoolerRoute",
                    Match = new RouteMatch
                    {
                        Path = _testServiceName.AbsolutePath,
                        Hosts = new[] { "example.org" },
                    },
                    Order = 3,
                    ClusterId = "MyCoolClusterId",
                    Metadata = new Dictionary<string, string>(),
                    Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                    },
                },
            };
            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Fact]
        public void BuildCluster_PartitionedService_Works()
        {
            const string TestClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(TestClusterId);

            SFYarpBackendService backendService = this.SimulateDiscoveredApp2Partition2ReplicasEach(_testAppName, _testSvcName, labels);
            List<PartitionWrapper> partitions = new List<PartitionWrapper>();
            List<ReplicaWrapper> replicas = new List<ReplicaWrapper>();
            foreach (var partition in backendService.FabricService.Partitions)
            {
                foreach (var replica in partition.Replicas)
                {
                    replicas.Add(replica.Replica);
                }
                partitions.Add(partition.Partition);
            }
            List<string> errors = new List<string>();
            /*
            var clusters = SFTestHelpers.ClusterWithDestinations(
                    backendService,
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[0], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[1], partitions[0]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[2], partitions[1]),
                    SFTestHelpers.BuildDestinationFromReplicaAndPartition(replicas[3], partitions[1]));
            */

            Dictionary<string, Dictionary<string, DestinationConfig>> destinations = SFYarpConfigProducer.BuildDestinations(backendService, errors); // or this.SimulateSFYarpDestinations(_testServiceName, _partitionId, _replicaId);
            var clusters = LabelsParserV2.BuildClusters(backendService, destinations, errors);

            var clusterConfig = new ClusterConfig
            {
                ClusterId = $"MyService123/{partitions[0].PartitionId.ToString()}",
                SessionAffinity = new SessionAffinityConfig
                {
                    Cookie = new SessionAffinityCookieConfig(),
                },
                HttpRequest = new ForwarderRequestConfig(),
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = false,
                        Interval = TimeSpan.FromSeconds(5),
                        Timeout = TimeSpan.FromSeconds(5),
                        Path = "/api/health",
                        Policy = "ConsecutiveFailures",
                    },
                    Passive = new PassiveHealthCheckConfig(),
                },
                Metadata = new Dictionary<string, string>
                {
                    { "Foo", "Bar" },
                    { "__SF.ApplicationTypeName", "CoreServices.SFYarpType" },
                    { "__SF.ApplicationName", "fabric:/CoreServices.SFYarp" },
                    { "__SF.ServiceTypeName", "EchoServiceType" },
                    { "__SF.ServiceName", "fabric:/CoreServices.SFYarp/EchoService" },
                },
                HttpClient = new HttpClientConfig
                {
                    WebProxy = new WebProxyConfig
                    {
                    },
                },
                Destinations = this.SimulateSFYarpDestinations(partitions.GetRange(0, 1), replicas.GetRange(0, 2), 1, 2),
            };

            var cluster2Config = new ClusterConfig
            {
                ClusterId = $"MyService123/{partitions[1].PartitionId.ToString()}",
                SessionAffinity = new SessionAffinityConfig
                {
                    Cookie = new SessionAffinityCookieConfig(),
                },
                HttpRequest = new ForwarderRequestConfig(),
                HealthCheck = new HealthCheckConfig
                {
                    Active = new ActiveHealthCheckConfig
                    {
                        Enabled = false,
                        Interval = TimeSpan.FromSeconds(5),
                        Timeout = TimeSpan.FromSeconds(5),
                        Path = "/api/health",
                        Policy = "ConsecutiveFailures",
                    },
                    Passive = new PassiveHealthCheckConfig(),
                },
                Metadata = new Dictionary<string, string>
                {
                    { "Foo", "Bar" },
                    { "__SF.ApplicationTypeName", "CoreServices.SFYarpType" },
                    { "__SF.ApplicationName", "fabric:/CoreServices.SFYarp" },
                    { "__SF.ServiceTypeName", "EchoServiceType" },
                    { "__SF.ServiceName", "fabric:/CoreServices.SFYarp/EchoService" },
                },
                HttpClient = new HttpClientConfig
                {
                    WebProxy = new WebProxyConfig
                    {
                    },
                },
                Destinations = this.SimulateSFYarpDestinations(partitions.GetRange(1, 1), replicas.GetRange(2, 2), 1, 2),
            };

            var expectedClusters = new List<ClusterConfig> { clusterConfig, cluster2Config };
            clusters.Should().BeEquivalentTo(expectedClusters);
        }

        [Fact]
        public void BuildRoutes_PartitionedService_Works()
        {
            const string testClusterId = "MyService123";
            var labels = SFTestHelpers.DummyLabels(testClusterId);

            SFYarpBackendService backendService = this.SimulateDiscoveredApp2Partition2ReplicasEach(_testAppName, _testSvcName, labels);
            List<string> errors = new List<string>();

            var routes = LabelsParserV2.BuildRoutes(backendService, errors);

            // TODO: Remove dependency of getting partitionIds from LabelsParserV2.BuildRoutes result
            var partition1Id = routes[0].Match.QueryParameters[0].Values[0];
            var partition2Id = routes[1].Match.QueryParameters[0].Values[0];

            var routeConfig1 = new RouteConfig
            {
                RouteId = $"{testClusterId}:{partition1Id}:MyRoute",
                Match = new RouteMatch
                {
                    Path = _testServiceName.AbsolutePath,
                    Hosts = new[] { "example.com" },
                    QueryParameters = new List<RouteQueryParameter>()
                    {
                        new RouteQueryParameter()
                        {
                            Name = "PartitionID",
                            Mode = QueryParameterMatchMode.Exact,
                            Values = new[] { partition1Id },
                        },
                    },
                },
                Order = 2,
                ClusterId = $"{testClusterId}/{partition1Id}",
                Metadata = new Dictionary<string, string>
                    {
                        { "Foo", "Bar" },
                    },
                Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                        new Dictionary<string, string> { { "QueryRemoveParameter", "PartitionID" } },
                    },
            };

            var routeConfig2 = new RouteConfig
            {
                RouteId = $"{testClusterId}:{partition2Id}:MyRoute",
                Match = new RouteMatch
                {
                    Path = _testServiceName.AbsolutePath,
                    Hosts = new[] { "example.com" },
                    QueryParameters = new List<RouteQueryParameter>()
                    {
                        new RouteQueryParameter()
                        {
                            Name = "PartitionID",
                            Mode = QueryParameterMatchMode.Exact,
                            Values = new[] { partition2Id },
                        },
                    },
                },
                Order = 2,
                ClusterId = $"{testClusterId}/{partition2Id}",
                Metadata = new Dictionary<string, string>
                    {
                        { "Foo", "Bar" },
                    },
                Transforms = new[]
                    {
                        new Dictionary<string, string> { { "PathRemovePrefix", _testServiceName.AbsolutePath } },
                        new Dictionary<string, string> { { "QueryRemoveParameter", "PartitionID" } },
                    },
            };
            var expectedRoutes = new List<RouteConfig> { routeConfig1, routeConfig2 };

            routes.Should().BeEquivalentTo(expectedRoutes);
        }

        [Fact]
        public void LoggingLabelParsingErrrors_Works()
        {
            var labels = new Dictionary<string, string>()
            {
                { "Yarp.Enable", "true" },
                { "Yarp.Backend.BackendId", "MyCoolClusterId" },
                { "Yarp.Backend.HealthCheck.Active.Enabled", "notbool" },
                { "Yarp.Backend.HealthCheck.Active.Interval", "1S" },
                { "Yarp.Backend.HealthCheck.Active.Timeout", "foobar" },
                { "Yarp.Routes.MyRoute.Order", "this is no number" },
                { "Yarp.Routes.MyRoute.MatchHeaders.[0].UnknownProperty", "some value" },
                { "Yarp.Routes..Order", "that was an empty route name" },
                { "Yarp.Routes..Hosts", "that was an empty route name" },
                { "Yarp.Routes.  .Hosts", "that was an empty route name" },
                { "Yarp.Routes..", "that was an empty route name" },
                { "Yarp.Routes.FunnyChars!.Hosts", "some value" },
                { "Yarp.Routes.'FunnyChars'.Order", "some value" },
                { "Yarp.Routes.FunnyChárs.Metadata", "some value" },
            };

            var errors = new List<string>();

            SFYarpBackendService backendService = this.SimulateDiscoveredApp2Partition2ReplicasEach(_testAppName, _testSvcName, labels);

            // Any label parsing errors will be logged once Yarp model is computed
            SFYarpConfigProducer.ComputeYarpModel(backendService, errors);
        }

        private List<ClusterConfig> SimulateBuildingSFYarpClusters(IReadOnlyDictionary<string, string> labels, List<string> errors)
        {
            SFYarpBackendService backendService = this.SimulateSFYarpBackendService(_testServiceName, labels);
            Dictionary<string, Dictionary<string, DestinationConfig>> destinations = SFYarpConfigProducer.BuildDestinations(backendService, errors); // or this.SimulateSFYarpDestinations(_testServiceName, _partitionId, _replicaId);
            var clusters = LabelsParserV2.BuildClusters(backendService, destinations, errors);
            return clusters;
        }

        // Creates a test backend service with 1 partition and 2 replicas
        private SFYarpBackendService SimulateSFYarpBackendService(Uri serviceName, IReadOnlyDictionary<string, string> labels)
        {
            var sfApp = this.SimulateDiscoveredApp(out DiscoveredServiceEx sfService);

            return new SFYarpBackendService
            {
                LastUsed = TimeSpan.FromSeconds(30),
                FabricApplication = sfApp,
                FabricService = sfService,
                ParsedServiceType = null,
                EffectiveLabels = null,
                LabelOverrides = null,
                FinalEffectiveLabels = labels,
            };
        }
        private Dictionary<string, DestinationConfig> SimulateSFYarpDestinations(List<string> partitionIds, List<string> replicaIds)
        {
            Dictionary<string, DestinationConfig> detinations = new Dictionary<string, DestinationConfig>();

            foreach (string partitionId in partitionIds)
            {
                foreach (string replicaId in replicaIds)
                {
                    string destinationKey = $"{partitionId}/{replicaId}";
                    DestinationConfig destinationVal = new DestinationConfig
                    {
                        Address = $"https://127.0.0.1/{partitionId}/{replicaId}",
                        Metadata = new Dictionary<string, string>
                        {
                            { "__SF.PartitionId", partitionId },
                            { "__SF.ReplicaId", replicaId },
                        },
                    };
                    detinations.Add(destinationKey, destinationVal);
                }
            }
            return detinations;
        }

        private Dictionary<string, DestinationConfig> SimulateSFYarpDestinations(List<PartitionWrapper> partitions, List<ReplicaWrapper> replicas, int numPartitions, int numReplicas)
        {
            var destinations = new Dictionary<string, DestinationConfig>();
            int replicaIndex = 0;
            for (int i = 0; i < partitions.Count(); i++)
            {
                for (int j = 0; j < numReplicas && replicaIndex < replicas.Count(); j++)
                {
                    string partitionId = partitions[i].PartitionId.ToString();
                    string replicaId = replicas[replicaIndex].Id.ToString();
                    destinations.Add($"{partitionId}/{replicaId}", new DestinationConfig
                    {
                        Address = $"https://127.0.0.1//{replicaId}",
                        Metadata = new Dictionary<string, string>
                        {
                            { "__SF.PartitionId", partitionId },
                            { "__SF.ReplicaId", replicaId },
                        },
                    });
                    replicaIndex++;
                }
            }
            return destinations;
        }

        private DiscoveredAppEx SimulateDiscoveredApp(out DiscoveredServiceEx sfService)
        {
            var sfyAppType = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey("CoreServices.SFYarpAppType"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey("1.16.01516.107-master-abc2ab1e"),
                Status = ApplicationTypeStatus.Available,
            };
            var echoServiceType = new ServiceTypeWrapper
            {
                ServiceTypeName = new ServiceTypeNameKey("SFYarpEchoType"),
                ServiceTypeKind = ServiceDescriptionKind.Stateless,
                ServiceManifestName = "EchoServicePkg",
                ServiceManifestVersion = "1.16.01516.107-master-abc2ab1e",
            };
            var sfyApp = new ApplicationWrapper
            {
                ApplicationName = new ApplicationNameKey(new Uri("fabric:/CoreServices.SFYarp")),
                ApplicationTypeName = sfyAppType.ApplicationTypeName,
                ApplicationTypeVersion = sfyAppType.ApplicationTypeVersion,
            };
            var echoService = new ServiceWrapper
            {
                ServiceName = new ServiceNameKey(new Uri("fabric:/CoreServices.SFYarp/EchoService")),
                ServiceTypeName = echoServiceType.ServiceTypeName,
                ServiceManifestVersion = echoServiceType.ServiceManifestVersion,
                ServiceKind = ServiceKind.Stateless,
            };
            var echoPartition = new PartitionWrapper
            {
                PartitionId = Guid.Parse("8c034a68-beba-4527-a206-323c831c61fb"),
            };
            var echoReplicas = new[]
            {
                new ReplicaWrapper
                {
                    Id = 132587450787126998,
                    ReplicaAddress = @"{""Endpoints"":{""Service Endpoint Secure"":""https://127.0.0.1/8c034a68-beba-4527-a206-323c831c61fb/132587450787126998""}}",
                    HealthState = HealthState.Ok,
                    ReplicaStatus = ServiceReplicaStatus.Ready,
                    Role = ReplicaRole.None,
                    ServiceKind = ServiceKind.Stateless,
                },
                new ReplicaWrapper
                {
                    Id = 132587449112860863,
                    ReplicaAddress = @"{""Endpoints"":{""Service Endpoint Secure"":""https://127.0.0.1/8c034a68-beba-4527-a206-323c831c61fb/132587449112860863""}}",
                    HealthState = HealthState.Ok,
                    ReplicaStatus = ServiceReplicaStatus.Ready,
                    Role = ReplicaRole.None,
                    ServiceKind = ServiceKind.Stateless,
                },
            };

            var discoveredSfyAppType = new DiscoveredAppType(sfyAppType);
            var discoveredEchoServiceType = new DiscoveredServiceType(echoServiceType);
            var discoveredSfyAppTypeEx = new DiscoveredAppTypeEx(
                discoveredSfyAppType,
                new Dictionary<ServiceTypeNameKey, DiscoveredServiceType>
                {
                    { echoServiceType.ServiceTypeName, discoveredEchoServiceType },
                });

            var discoveredEchoService = new DiscoveredService(discoveredEchoServiceType, echoService);
            var discoveredEchoReplicas = echoReplicas.Select(e => new DiscoveredReplica(e)).ToList();
            DiscoveredPartition[] partitions = new[]
                {
                    new DiscoveredPartition(echoPartition, discoveredEchoReplicas),
                };
            var discoveredEchoServiceEx = new DiscoveredServiceEx(
                discoveredEchoService,
                partitions);
            sfService = discoveredEchoServiceEx;
            var discoveredSfyApp = new DiscoveredApp(sfyApp);
            var discoveredSfyAppEx = new DiscoveredAppEx(
                discoveredSfyApp,
                discoveredSfyAppTypeEx,
                new Dictionary<ServiceNameKey, DiscoveredService>
                {
                    { echoService.ServiceName, discoveredEchoServiceEx },
                });

            return discoveredSfyAppEx;
        }

        private SFYarpBackendService SimulateDiscoveredApp2Partition2ReplicasEach(string appName, string serviceName, IReadOnlyDictionary<string, string> labels)
        {
            var appType = new ApplicationTypeWrapper
            {
                ApplicationTypeName = new ApplicationTypeNameKey($"{appName}Type"),
                ApplicationTypeVersion = new ApplicationTypeVersionKey("1.16.01516.107-master-abc2ab1e"),
                Status = ApplicationTypeStatus.Available,
            };
            var serviceType = new ServiceTypeWrapper
            {
                ServiceTypeName = new ServiceTypeNameKey($"{serviceName}Type"),
                ServiceTypeKind = ServiceDescriptionKind.Stateful,
                ServiceManifestName = $"{serviceName}Pkg",
                ServiceManifestVersion = "1.16.01516.107-master-abc2ab1e",
            };

            var sfyApp = SFTestHelpers.CreateApp_1StatelfulService_2Partition_2ReplicasEach(
            appName,
            appType.ApplicationTypeVersion,
            serviceName,
            out ServiceWrapper service,
            out List<ReplicaWrapper> replicas,
            out List<PartitionWrapper> partitions);

            var discoveredSfyAppType = new DiscoveredAppType(appType);
            var discoveredServiceType = new DiscoveredServiceType(serviceType);
            var discoveredSfyAppTypeEx = new DiscoveredAppTypeEx(
                discoveredSfyAppType,
                new Dictionary<ServiceTypeNameKey, DiscoveredServiceType>
                {
                    { serviceType.ServiceTypeName, discoveredServiceType },
                });
            var discoveredService = new DiscoveredService(discoveredServiceType, service);
            var discoveredReplicas = replicas.Select(e => new DiscoveredReplica(e)).ToList();
            DiscoveredPartition[] partitions1 = new[]
                {
                    new DiscoveredPartition(partitions[0], discoveredReplicas.GetRange(0, 2)),
                    new DiscoveredPartition(partitions[1], discoveredReplicas.GetRange(2, 2)),
                };
            var discovereServiceEx = new DiscoveredServiceEx(
                discoveredService,
                partitions1);

            var discoveredSfyApp = new DiscoveredApp(sfyApp);
            var discoveredSfyAppEx = new DiscoveredAppEx(
                discoveredSfyApp,
                discoveredSfyAppTypeEx,
                new Dictionary<ServiceNameKey, DiscoveredService>
                {
                    { service.ServiceName, discoveredService },
                });

            return new SFYarpBackendService
            {
                LastUsed = TimeSpan.FromSeconds(30),
                FabricApplication = discoveredSfyAppEx,
                FabricService = discovereServiceEx,
                ParsedServiceType = null,
                EffectiveLabels = null,
                LabelOverrides = null,
                FinalEffectiveLabels = labels,
            };
        }
        private Dictionary<string, Dictionary<string, DestinationConfig>> SimulateSFYarpDestinations(Uri serviceName, string partitionId, string replicaId)
        {
            var destinations = new Dictionary<string, Dictionary<string, DestinationConfig>>();
            PartitionWrapper partition = SFTestHelpers.FakePartition(partitionId);
            ReplicaWrapper replica = SFTestHelpers.FakeReplica(serviceName, long.Parse(replicaId));

            var destinationKV = SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica, partition);
            var destination = new Dictionary<string, DestinationConfig>();
            destination.Add(destinationKV.Key, destinationKV.Value);

            destinations.Add(partitionId, destination);
            return destinations;
        }
        private Dictionary<string, Dictionary<string, DestinationConfig>> SimulateSFYarpDestinations2Partition2ReplicasEach(Uri serviceName, string partitionId, string replicaId)
        {
            var destinations = new Dictionary<string, Dictionary<string, DestinationConfig>>();
            PartitionWrapper partition = SFTestHelpers.FakePartition(partitionId);
            ReplicaWrapper replica = SFTestHelpers.FakeReplica(serviceName, long.Parse(replicaId));

            var destinationKV = SFTestHelpers.BuildDestinationFromReplicaAndPartition(replica, partition);
            var destination = new Dictionary<string, DestinationConfig>();
            destination.Add(destinationKV.Key, destinationKV.Value);

            destinations.Add(partitionId, destination);
            return destinations;
        }
    }
}
