// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;

namespace Yarp.ServiceFabric.RemoteConfig.Contract.Tests
{
    public class SerializationTests
    {
        private readonly JsonSerializerOptions jsonOptions;

        public SerializationTests()
        {
            this.jsonOptions = new JsonSerializerOptions()
                .ApplySFYarpRemoteConfigSettings();
        }

        [Fact]
        public void RoundTrip_Works()
        {
            // Arrange
            var (model, _) = CreateSampleConfig();

            // Act
            var json = JsonSerializer.Serialize(model, this.jsonOptions);
            var output = JsonSerializer.Deserialize<RemoteConfigResponseDto>(json, this.jsonOptions);
            var json2 = JsonSerializer.Serialize(output, this.jsonOptions);

            // Assert
            json2.Should().Be(json);
            DiffModels(model, output);
        }

        [Fact]
        public void Deserialize_Works()
        {
            // Arrange
            var (model, json) = CreateSampleConfig();

            // Act
            var deserialized = JsonSerializer.Deserialize<RemoteConfigResponseDto>(json, this.jsonOptions);

            // Assert
            DiffModels(model, deserialized);
        }

        private static (RemoteConfigResponseDto Model, string Json) CreateSampleConfig()
        {
            var model = new RemoteConfigResponseDto
            {
                Clusters = new List<ClusterConfig>
                {
                    new ClusterConfig
                    {
                        ClusterId = "cluster1",
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["destination1"] = new DestinationConfig
                            {
                                Address = "https://127.0.0.1:65534/",
                                Metadata = new Dictionary<string, string>
                                {
                                    ["cluster1/destination1/key1"] = "value1",
                                },
                            },
                        },
                        HttpRequest = new ForwarderRequestConfig
                        {
                            ActivityTimeout = TimeSpan.FromSeconds(30),
                        },
                        HealthCheck = new HealthCheckConfig
                        {
                            Active = new ActiveHealthCheckConfig
                            {
                                Enabled = true,
                                Interval = TimeSpan.FromSeconds(30),
                                Timeout = TimeSpan.FromSeconds(10),
                                Path = "api/health",
                            },
                        },
                        LoadBalancingPolicy = LoadBalancingPolicies.Random,
                        Metadata = new Dictionary<string, string>
                        {
                            ["cluster1/key1"] = "value2",
                        },
                    },
                },
                Routes = new List<RouteConfig>
                {
                    new RouteConfig
                    {
                        ClusterId = "cluster1",
                        RouteId = "cluster1/route1",
                        Order = 1,
                        Match = new RouteMatch
                        {
                            Hosts = new List<string>() { "localhost" },
                            Path = "/{**catchall}",
                        },
                        Metadata = new Dictionary<string, string>
                        {
                            ["route1/key1"] = "value3",
                        },
                    },
                },
                AsOf = new DateTimeOffset(2021, 5, 25, 22, 46, 05, TimeSpan.FromHours(-7)),
            };

            var json = @"
{
  ""clusters"": [
    {
      ""clusterId"": ""cluster1"",
      ""loadBalancingPolicy"": ""Random"",
      ""sessionAffinity"": null,
      ""healthCheck"": {
        ""passive"": null,
        ""active"": {
          ""enabled"": true,
          ""interval"": ""PT30S"",
          ""timeout"": ""PT10S"",
          ""policy"": null,
          ""path"": ""api/health""
        },
        ""availableDestinationsPolicy"": null
      },
      ""httpClient"": null,
      ""httpRequest"": {
        ""timeout"": ""PT30S"",
        ""version"": null,
        ""allowResponseBuffering"": null
      },
      ""destinations"": {
        ""destination1"": {
          ""address"": ""https://127.0.0.1:65534/"",
          ""health"": null,
          ""metadata"": {
            ""cluster1/destination1/key1"": ""value1""
          }
        }
      },
      ""metadata"": {
        ""cluster1/key1"": ""value2""
      }
    }
  ],
  ""routes"": [
    {
      ""routeId"": ""cluster1/route1"",
      ""match"": {
        ""methods"": null,
        ""hosts"": [
          ""localhost""
        ],
        ""path"": ""/{**catchall}"",
        ""headers"": null
      },
      ""order"": 1,
      ""clusterId"": ""cluster1"",
      ""authorizationPolicy"": null,
      ""corsPolicy"": null,
      ""metadata"": {
        ""route1/key1"": ""value3""
      },
      ""transforms"": null
    }
  ],
  ""asOf"": ""2021-05-25T22:46:05-07:00""
}";

            return (model, json);
        }

        private static void DiffModels(RemoteConfigResponseDto a, RemoteConfigResponseDto b)
        {
            DiffClusters(a.Clusters, b.Clusters);
            DiffRoutes(a.Routes, b.Routes);
            a.AsOf.Should().Be(b.AsOf);

            static void DiffClusters(IReadOnlyList<ClusterConfig> a, IReadOnlyList<ClusterConfig> b)
            {
                a.Count.Should().Be(b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    DiffCluster(a[i], b[i]);
                }

                static void DiffCluster(ClusterConfig a, ClusterConfig b)
                {
                    a.Equals(b).Should().BeTrue();
                }
            }

            static void DiffRoutes(IReadOnlyList<RouteConfig> a, IReadOnlyList<RouteConfig> b)
            {
                a.Count.Should().Be(b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    DiffRoute(a[i], b[i]);
                }

                static void DiffRoute(RouteConfig a, RouteConfig b)
                {
                    a.Equals(b).Should().BeTrue();
                }
            }
        }
    }
}
