# ServiceFabricYarp 1.1.0


Table of Contents
=================
  * [Background](#background)
  * [Limitations](#limitations)
  * [How it works](#how-it-works)
  * [Using SF YarpProxy Application](#using-sf-yarpproxy-application)
      * [Deploy it using PowerShell](#deploy-it-using-powershell)
      * [URI format for addressing services by using the reverse proxy](#uri-format-for-addressing-services-by-using-the-reverse-proxy)
      * [HTTPS](#https-tls)
      * [Add the right labels to your services](#add-the-right-labels-to-your-services)
      * [Supported Labels](#supported-labels)
      * [Replica endpoint selection](#replica-endpoint-selection)
      * [Fabric Discovery Service Configuration](#fabric-discovery-service-configuration)
      * [Stateful services](#stateful-services)
      * [Metadata](#metadata)
      * [Sample Test Application](#sample-test-application)
      * [Internal Telemetry](#internal-telemetry)
      * [Tracing](#tracing)

  * [YARP Reverse Proxy for Service Fabric Integration](#yarp-reverse-proxy-for-service-fabric-integration)
      * [Pre-reqs for development machine](#pre-reqs-for-development-machine)
      * [Building and Unit Testing](#building-and-unit-testing)
      * [Project Structure](#project-structure)
      * [Running Locally](#running-locally)
 

## Background
The reverse proxy is an application, supplied out of band from the service fabric distribution, that customers deploy to their clusters and handles proxying traffic to backend services. The service, that potentially runs on every node in the cluster, takes care of handling endpoint resolution, automatic retry, and other connection failures on behalf of the clients. The reverse proxy can be configured to apply various policies as it handles requests from client services.

Using a reverse proxy allows the client service to use any client-side HTTP communication libraries and does not require special resolution and retry logic in the service. The reverse proxy is mostly a terminating endpoint for the TLS connections

> Note that, at this time, this is a reverse proxy built-in replacement and not a generic service fabric “gateway” able to handle partition queries, but that might be added (via customer written plugins or similar) in the future.

## Limitations
* YarpProxy app is only supported on Windows
* No TCP proxying support ONLY HTTP.
* Limited partitioning support. Partitions are enumerated to retrieve all of the nested replicas/instances, but the partitioning key is not handled in any way. In other words there is no [partition scheme](https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-concepts-partitioning#get-started-with-partitioning) support. Instead to send a request to a replica running on a certain partition need to pass the partition GUID as a query parameter. More details can be found in [Stateful services](#stateful-services).
- Only one endpoint per each SF service's replica/instance is considered and gets converted to a [DestinationConfig](https://github.com/microsoft/reverse-proxy/blob/464a3dcb5a6a69cf256fb2d83700e3edbb39d78b/src/ReverseProxy/Configuration/DestinationConfig.cs#L13).


## How it works 
As of this release, the services need to be explicitly exposed via [service extension labels](#add-the-right-labels-to-your-services), enabling the proxying (HTTP) functionality for a particular service and endpoint. With the right labels’ setup, the reverse proxy will expose one or more endpoints on the local nodes for client services to use. The ports can then be exposed to the load balancer in order to get the services available outside of the cluster. SF YarpProxy app has both http and https ports being listened on by default. For HTTPS the required certificates should be already deployed to the nodes where the proxy is running.

## Using SF YarpProxy Application

You can clone the repo, build, and deploy or simply grab the latest [ZIP/SFPKG application](https://github.com/microsoft/service-fabric-yarp/releases/latest) from Releases section, modify configs, and deploy.

![alt text](/docs/yarp-cluster-view.png "Cluster View UI")

![alt text](/docs/yarp-service-view.png "Cluster Service View UI")

## Deploy it using PowerShell  

After either downloading the SF app package from the releases or cloning the repo and building, you need to adjust the configuration settings to meet to your needs (this means changing settings in Settings.xml, ApplicationManifest.xml and any other changes needed).

>If you need a quick test cluster, you can deploy a test Service Fabric managed cluster following the instructions from here: [SFMC](https://docs.microsoft.com/en-us/azure/service-fabric/quickstart-managed-cluster-template), or via this template if you already have a client certificate and thumbprint available: [Deploy](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fservice-fabric-cluster-templates%2Fmaster%2FSF-Managed-Basic-SKU-1-NT%2Fazuredeploy.json)

>Retrieve the cluster certificate TP using:  $serverThumbprint = (Get-AzResource -ResourceId /subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCEGROUP/providers/Microsoft.ServiceFabric/managedclusters/$CLUSTERNAME).Properties.clusterCertificateThumbprints

```PowerShell

#cd to the top level directory where you downloaded the package zip
cd \downloads

#Expand the zip file

Expand-Archive .\service-fabric-yarp.zip -Force

#cd to the directory that holds the application package

cd .\service-fabric-yarp\windows\

#create a $appPath variable that points to the application location:
#E.g., for Windows deployments:

$appPath = "C:\downloads\service-fabric-yarp\windows\YarpProxyApp"

#Connect to target cluster, for example:

Connect-ServiceFabricCluster -ConnectionEndpoint @('sf-win-cluster.westus2.cloudapp.azure.com:19000') -X509Credential -FindType FindByThumbprint -FindValue '[Client_TP]' -StoreLocation LocalMachine -StoreName 'My' # -ServerCertThumbprint [Server_TP]

# Use this to remove a previous YarpProxy Application
#Remove-ServiceFabricApplication -ApplicationName fabric:/YarpProxyApp -Force
#Unregister-ServiceFabricApplicationType -ApplicationTypeName YarpProxyAppType -ApplicationTypeVersion 1.1.0 -Force

#Copy and register and run the YarpProxy Application
Copy-ServiceFabricApplicationPackage -CompressPackage -ApplicationPackagePath $appPath # -ApplicationPackagePathInImageStore YarpProxyApp
Register-ServiceFabricApplicationType -ApplicationPathInImageStore YarpProxyApp

#Fill the right values that are suitable for your cluster and application (the default ones below will work without modification if you used a Service Fabric managed cluster Quickstart template with one node type. Adjust the placement constraints to use other node types)
$p = @{
    YarpProxy_InstanceCount="1"
    YarpProxy_HttpPort="8080"
    #YarpProxy_HttpsPort="443" #By default https port is still being listened on. Reference application manifest
    #YarpProxy_PlacementConstraints="NodeType == NT2"
}
$p

New-ServiceFabricApplication -ApplicationName fabric:/YarpProxyApp -ApplicationTypeName YarpProxyAppType -ApplicationTypeVersion 1.1.0 -ApplicationParameter $p


#OR if updating existing version:  

Start-ServiceFabricApplicationUpgrade -ApplicationName fabric:/YarpProxyApp -ApplicationTypeVersion 1.1.0 -ApplicationParameter $p -Monitored -FailureAction rollback 
```  

## URI format for addressing services by using the reverse proxy
The SF YARP reverse proxy uses a specific uniform resource identifier (URI) format to identify the service partition to which the incoming request should be forwarded:

```
http(s)://<Cluster FQDN | internal IP>:Port/<ServiceInstanceName>/<Suffix path>?PartitionID=<PartitionGUID>

```

- `http(s):` The reverse proxy can be configured to accept HTTP or HTTPS traffic. For HTTPS forwarding, refer to [HTTPS section](#https-tls).
- `Cluster fully qualified domain name (FQDN) | internal IP:` For external clients, you can configure the reverse proxy so that it is reachable through the cluster domain, such as mycluster.eastus.cloudapp.azure.com. The reverse proxy can be configured to run on every node. For internal traffic, the reverse proxy can be reached on localhost or on any internal node IP, such as 10.0.0.1.
- `Port:` This is the port, such as 8080, that has been specified for the reverse proxy.
- `ServiceInstanceName:` This is the fully-qualified name of the deployed service instance that you are trying to reach without the "fabric:/" scheme. For example, to reach the fabric:/myapp/myservice/ service, you would use myapp/myservice.
- `Suffix path:` This is the actual URL path, such as myapi/values/add/3, for the service that you want to connect to.
- `PartitionGUID:`  The partition ID GUID of the partition that you want to reach.


## HTTPS (TLS)
SF YARP reverse proxy listens on https port (443) by default and can be configured in ApplicationManifest.xml. SF YARP takes care of TLS certificate binding on its own. For HTTPS the required certificates should be already deployed to the nodes where the reverse proxy is running as is the case with any other SF application.

The certificates need to be created with the CN and DNS Names configured with the SF cluster's FQDN (i.e "sf-win-cluster.westus2.cloudapp.azure.com", "localhost", etc.) and added in the following certificate store cert:\LocalMachine\My for each node were YarpProxy service is running. When a connection request is sent over HTTPS to YarpProxy it  will select a TLS server authentication certificate (if available) for the specified inbound TLS SNI host name.

To setup certs on each node in a remote managed cluster, this can be done using [Azure Key Vault Extension](https://docs.microsoft.com/en-us/azure/service-fabric/how-to-managed-cluster-application-secrets) either from Azure portal or ARM templates. 

Need to also make sure certificate has proper ACL to be retrieved by YarpProxy process running under the configured local account (by default SF applications run under Network Service account) so that the private key can be accessed during the SNI step in TLS handshake.



## Add the right labels to your services

Service Fabric YARP integration is enabled and configured per each SF service. The configuration is specified in the `Service Manifest` as a service extension named `Yarp` containing a set of labels defining specific YARP parameters. The parameter's name is set as `Key` attribute of `<Label>` element and the value is the element's content.

This is a sample SF enabled service showing a subset of the supported labels. If the SF name is fabric:/pinger/PingerService, the endpoint will be exposed at that prefix: '/pinger/PingerService/'

```xml
  ...
  <ServiceTypes>
    <StatelessServiceType ServiceTypeName="PingerServiceType" UseImplicitHost="true">
      <Extensions>
        <Extension Name="Yarp">
          <Labels xmlns="http://schemas.microsoft.com/2015/03/fabact-no-schema">
            <Label Key="Yarp.Enable">true</Label>
            <Label Key="Yarp.Routes.defaultRoute.Path">/{**catchall}</Label>
            <Label Key='Yarp.Backend.HealthCheck.Active.Enabled'>true</Label>
            <Label Key='Yarp.Backend.HealthCheck.Active.Path'>/</Label>
            <Label Key='Yarp.Backend.HealthCheck.Active.Interval'>00:00:10</Label>
            <Label Key='Yarp.Backend.HealthCheck.Active.Timeout'>00:00:30</Label>
          </Labels>
        </Extension>
      </Extensions>
    </StatelessServiceType>
  </ServiceTypes>
  ...
```

---

The only required label to expose a service via the reverse proxy is the **Yarp.Enable** set to true. Setting only this label will expose the service on a well-known path and handle the basic scenarios.

If you need to change the routes then you can add different labels configuring them.


## Supported Labels

There are 3 types of labels that are currently supported that allow users to configure the expected behavior for YARP and service fabric integration. These include: route, backend/cluster and service fabric integration labels. 

More information regarding the Route and Backend/Cluster parameters can be found in the [YARP documentation](https://microsoft.github.io/reverse-proxy/articles/config-files.html). All SF integration specific labels will be explained below.

### Service fabric integration section
- `Yarp.Enable` - indicates whether the service opt-ins to serving traffic through YARP. Default `false`
- `Yarp.EnableDynamicOverrides` - indicates whether application parameters replacement is enabled on the service. Default `false`

### Route section

Multiple routes can be defined in a SF service configuration with the following parameters:
- `Yarp.Routes.<routeName>.Path` - configures path-based route matching. The value directly assigned to [RouteMatch.Path](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteMatch.cs#L29) property and the standard route matching logic will be applied. `{**catch-all}` path may be used to route all requests.
- `Yarp.Routes.<routeName>.Hosts` - configures `Host` based route matching. Multiple hosts should be separated by comma. The value is split into a list of host names which is then directly assigned to [RouteMatch.Hosts](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteMatch.cs#L24) property and the standard route matching logic will be applied.
- `Yarp.Routes.<routeName>.Methods` - configures `Methods` based route matching. Only match requests that use these optional HTTP methods. E.g. GET, POST. Multiple methods should be separated by comma. The value is split into a list of HTTP methods which is then directly assigned to [RouteMatch.Methods](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteMatch.cs#L18) property and the standard route matching logic will be applied.
- `Yarp.Routes.<routeName>.MatchHeaders.[<Index>].*` - configures `Header` based route matching. Only match requests that contain all of these headers. Can configure each header separately by passing a unique index value. The parameters configured at each index will be used to create a [RouteHeader](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteHeader.cs#L13) 
object. A list of RouteHeader objects will be created and directly assigned to [RouteMatch.Headers](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteMatch.cs#L39) property and the standard route matching logic will be applied.
- `Yarp.Routes.<routeName>.MatchQueries.[<Index>].*` - configures `Query` based route matching.  Only match requests that contain all of these query parameters. Can configure each query separately by passing a unique index value. The parameters configured at each index will be used to create a [RouteQueryParameter](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteQueryParameter.cs#L13) 
object. A list of RouteQueryParameter objects will be created and directly assigned to [RouteMatch.QueryParameters ](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteMatch.cs#L34) property and the standard route matching logic will be applied.
- `Yarp.Routes.<routeName>.Order` - configures an order value for this route. Routes with lower numbers take precedence over higher numbers. Parameter will be directly assigned to [RouteConfig.Order](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteConfig.cs#L31) property. Optional parameter
- `Yarp.Routes.<routeName>.Transforms.[<Index>].*` -configures parameters used to transform the request and response. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/transforms.html). Can configure each transform separately by passing a unique index value. The parameters configured at each index will be used to create a Transform dictionary object. A list of Transform dictionary objects will be created and directly assigned to [RouteConfig.Transforms ](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteConfig.cs#L63) property.
- `Yarp.Routes.<routeName>.AuthorizationPolicy` - configures the name of the AuthorizationPolicy to apply to this route. If not set then only the FallbackPolicy will apply. Parameter will be directly assigned to [RouteConfig.AuthorizationPolicy](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteConfig.cs#L45) property. Optional parameter
- `Yarp.Routes.<routeName>.CorsPolicy` - configures the name of the CorsPolicy to apply to this route. If not set then the route won't be automatically matched for cors preflight requests. Parameter will be directly assigned to [RouteConfig.CorsPolicy](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteConfig.cs#L53) property. Optional parameter
- `Yarp.Routes.<routeName>.Metadata` - configures arbitrary key-value pairs that further describe this route. Parameter will be directly assigned to [RouteConfig.Metadata](https://github.com/microsoft/reverse-proxy/blob/dce0b614ce3b3daf4a3cb23254c8d1c626a502ec/src/ReverseProxy/Configuration/RouteConfig.cs#L58) property. Optional parameter
- `<routeName>` can contain an ASCII letter, a number, or '_' and '-' symbols.
- `[<Index>]` pattern simulates indexing in an array. Only integer indexing is supported and indexing starts at 0.

Each route requires a `Path` or `Host` (or both). If both of them are specified, then a request is matched to the route only when both of them are matched. In addition to these, a route can also specify one or more headers that must be present on the request.

Route matching is based on the most specific routes having highest precedence. If a route contains multiple route matching rules then look at the [YARP route documentation](https://microsoft.github.io/reverse-proxy/articles/header-routing.html#precedence) on the default route matching precedence order.  Explicit ordering can be achieved using the `Order` field, with lower values taking higher priority.


Example:
```XML
<Label Key="Yarp.Routes.route-A1.Path">/api</Label>
<Label Key="Yarp.Routes.route-B2.Path">/control-api</Label>
<Label Key="Yarp.Routes.route-B2.Hosts">example.com,anotherexample.com</Label>
<Label Key="Yarp.Routes.ExampleRoute.Path">/exampleroute</Label>
<Label Key="Yarp.Routes.ExampleRoute.Metadata.Foo">Bar</Label>
<Label Key="Yarp.Routes.ExampleRoute.Methods">GET,PUT</Label>
<Label Key="Yarp.Routes.ExampleRoute.Order">2</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchQueries.[0].Mode">Exact</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchQueries.[0].Name">orgID</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchQueries.[0].Values">123456789</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchQueries.[0].IsCaseSensitive">true</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchHeaders.[0].Mode">ExactHeader</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchHeaders.[0].Name">x-company-key</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchHeaders.[0].Values">contoso</Label>
<Label Key="Yarp.Routes.ExampleRoute.MatchHeaders.[0].IsCaseSensitive">true</Label>
<Label Key="Yarp.Routes.ExampleRoute.Transforms.[0].ResponseHeader">X-Foo</Label>
<Label Key="Yarp.Routes.ExampleRoute.Transforms.[0].Append">Bar</Label>
<Label Key="Yarp.Routes.ExampleRoute.Transforms.[0].When">Always</Label>
<Label Key="Yarp.Routes.ExampleRoute.Transforms.[1].ResponseHeader">X-Ping</Label>
<Label Key="Yarp.Routes.ExampleRoute.Transforms.[1].Append">Pong</Label>
<Label Key="Yarp.Routes.ExampleRoute.Transforms.[1].When">Success</Label>
<Label Key="Yarp.Routes.ExampleRoute.AuthorizationPolicy">Policy1</Label>
<Label Key="Yarp.Routes.ExampleRoute.CorsPolicy">Policy1</Label>

```

### Backend section

- `Yarp.Backend.LoadBalancingPolicy` - configures YARP load balancing policy. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/load-balancing.html). Optional parameter
- `Yarp.Backend.SessionAffinity.*` - configures YARP session affinity. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/session-affinity.html). Optional parameter
- `Yarp.Backend.HttpRequest.*` - sets proxied HTTP request properties. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/http-client-config.html#httprequest) in 'HttpRequest' section. Optional parameter
- `Yarp.Backend.HttpClient.*` - sets HTTP client properties. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/http-client-config.html#httpclient) in 'HttpClient' section. Optional parameter
- `Yarp.Backend.HealthCheck.Active.*` - configures YARP active health checks to be run against the given service. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/dests-health-checks.html#active-health-checks). Optional parameter
- `Yarp.Backend.HealthCheck.Active.ServiceFabric.ListenerName` - sets an explicit listener name for the health probing endpoint for each replica/instance that is used to probe replica/instance health state and is stored on the `Destination.Health` property in YARP's model. Optional parameter
- `Yarp.Backend.HealthCheck.Passive.*` - configures YARP passive health checks to be run against the given service. Available parameters and their meanings are provided on [the respective documentation page](https://microsoft.github.io/reverse-proxy/articles/dests-health-checks.html#passive-health-checks). Optional parameter
- `Yarp.Backend.Metadata.*` - sets the cluster's metadata. Optional parameter
- `Yarp.Backend.BackendId` - overrides the cluster's Id. Default cluster's Id is the SF service name. Optional parameter
- `Yarp.Backend.ServiceFabric.ListenerName` - sets an explicit listener name for the main service's endpoint for each replica/instance that is used to route client requests to and is stored on the `Destination.Address` property in YARP's model. Optional parameter
- `Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode` - sets stateful replica selection mode. Supported values `All`, `PrimaryOnly`, `SecondaryOnly`. Values `All` and `SecondaryOnly` mean that the active secondary replicas will also be eligible for receiving client requests. Default value `Primary`
- Labels with `ServiceFabric` are service fabric related configuration and not specific to YARP

> NOTE: Label values can use the special syntax `[AppParamName]` to reference an application parameter with the name given within square brackets. This is consistent with Service Fabric conventions, see e.g. [using parameters in Service Fabric](https://docs.microsoft.com/azure/service-fabric/service-fabric-how-to-specify-port-number-using-parameters).


## Replica endpoint selection
A SF replica/instance can expose several endpoints for client requests and health probes, however SF Yarp currently supports only one endpoint of each respective type. The logic selecting that single endpoint is controlled by the `ListenerName` parameter. A listener name is simply a key in a key-value endpoint list published in form of an "address" string for each replica/instance. If it's specified, SF integration will find and take the respective endpoint URI identified by this key. If the listener name is not specified it will sort that key-value list by the key (i.e. by listener name) in `Ordinal` order and take the first endpoint from the sorted list that matches the configured allowed schemed predicate (i.e https, http) defined in the SF discovery options. By default allows both https and http endpoints  to be selected. More details below on how to configure this option as part of SF discovery options. For stateful services also have the ability to decide on the replica selection mode by the `StatefulReplicaSelectionMode` parameter.

There are several parameters specifying listener names for different endpoint types:
- `Yarp.Backend.ServiceFabric.ListenerName` - sets an explicit listener name for the main service's endpoint
- `Yarp.Backend.HealthCheck.Active.ServiceFabric.ListenerName` - sets an explicit listener name for the health probing endpoint

## Fabric Discovery service configuration
The following SF YARP parameters influence Service Fabric dynamic service discovery and
can be set in the configuration section `FabricDiscovery`:

- `FullRefreshPollPeriodInSeconds` - indicates 
how long to wait between complete refreshes of the Service Fabric topology, in seconds. Default `300s`
- `AbortAfterTimeoutInSeconds` - terminate the primary if Service Fabric topology discovery is taking longer than this amount. Default `600s`
- `AbortAfterConsecutiveFailures` - terminate the primary after it encounters this number of consecutive failures. Default `3`
- `AllowInsecureHttp` - indicates whether to discover unencrypted HTTP (i.e. non-HTTPS) endpoints. Default `true`

### Config example
The following is an example of an `ApplicationManifest.xml` file with `FabricDiscovery` section.
```XML
<Parameters>
  <Parameter Name="FabricDiscovery_InstanceCount" DefaultValue="1" />
  <Parameter Name="FabricDiscovery_AbortAfterTimeoutInSeconds" DefaultValue="600" />
  <Parameter Name="FabricDiscovery_AbortAfterConsecutiveFailures" DefaultValue="3" />
  <Parameter Name="FabricDiscovery_FullRefreshPollPeriodInSeconds" DefaultValue="300" />
</Parameters>

  <ServiceManifestImport>
    <ServiceManifestRef ServiceManifestName="FabricDiscoveryServicePkg" ServiceManifestVersion="1.1.0" />
    <ConfigOverrides>
      <ConfigOverride Name="Config">
        <Settings>
          <Section Name="FabricDiscovery">
            <Parameter Name="AbortAfterTimeoutInSeconds" Value="[FabricDiscovery_AbortAfterTimeoutInSeconds]" />
            <Parameter Name="AbortAfterConsecutiveFailures" Value="[FabricDiscovery_AbortAfterConsecutiveFailures]" />
            <Parameter Name="FullRefreshPollPeriodInSeconds" Value="[FabricDiscovery_FullRefreshPollPeriodInSeconds]" />
          </Section>
        </Settings>
      </ConfigOverride>
    </ConfigOverrides>
  </ServiceManifestImport>

```

## Stateful services
A stateful/partitioned service maintains a mutable, authoritative state beyond the request and its response. In Service Fabric, a stateful service is modeled as a set of replicas in a partition which allows you to split the data across partitions, a concept known as data partitioning or sharding. A particular service partition is responsible for a portion of the complete state (data) of the service. The replicas of each partition are spread across the cluster's nodes, which allows your named service's state to scale. 

SF YARP does support request to partitioned services. 
For a stateful service with >1 partitions all configured parameters will be used to create a separate YARP Route and Cluster config per partition. An implicit `Query` route matching rule will be added for each route so that the request can be forwarded to a particular partition. In order to send a request to a specific partition need to pass the specific [partition GUID](#uri-format-for-addressing-services-by-using-the-reverse-proxy) as a query parameter. The GUID can be retrieved using the SF PowerShell command "Get-ServiceFabricPartition" or from Service Fabric Explorer (SFX). When a request is sent to a certain partition it will be handled by one of the replicas running on that partition.

> NOTE: If a stateful service only has 1 partition then do not include PartitionID as a query parameter in the request. 

## Metadata 
SF Yarp includes known metadata to elements (e.g. Clusters, Destinations) so that any necessary info regarding a SF service is available from the YARP pipeline. 

| Applies To | Metadata Key   | Meaning   | Example value   |
| :---:   | :---: | :---: | :---: |
| Cluster/Backend  | __SF.ApplicationTypeName | Service Fabric Application Type of the SF service from which the YARP cluster was created | MyAppType |
| Cluster/Backend | __SF.ApplicationName   | Service Fabric app name of the SF service from which the YARP cluster was created   | fabric:/MyApp |
| Cluster/Backend | __SF.ServiceTypeName | Service Fabric Service Type of the SF service from which the YARP cluster was created | MySvcType |
| Cluster/Backend | __SF.ServiceName | Service Fabric service name from which the YARP cluster was created | fabric:/MyApp/MySvc |
| Destination | __SF.PartitionId | Partition id of the SF replica | < guid > |
| Destination | __SF.ReplicaId | ReplicaId/InstanceId for stateful/stateless services | < guid >
 
## Sample Test application

A sample test application, that is included in the release, can be deployed to test everything is working alright. After deployment, you should be able to reach it at:

http://<Cluster FQDN | internal IP>:8080/pinger0/PingerService/id


```Powershell

# Sample pinger app for validating (navigate to /pinger0/PingerService/id on https)
#Remove-ServiceFabricApplication -ApplicationName fabric:/pinger$i -Force
#Unregister-ServiceFabricApplicationType -ApplicationTypeName PingerApplicationType -ApplicationTypeVersion 1.0 -Force

$appPath = "C:\downloads\service-fabric-yarp\windows\pinger-yarp"

Copy-ServiceFabricApplicationPackage -CompressPackage -ApplicationPackagePath $appPath -ApplicationPackagePathInImageStore pinger-yarp
Register-ServiceFabricApplicationType -ApplicationPathInImageStore pinger-yarp

$p = @{
    "Pinger_Instance_Count"="1"
    "Pinger_Port"="7000"
    #"Pinger_PlacementConstraints"= "NodeType == NT2"
}

New-ServiceFabricApplication -ApplicationName fabric:/pinger0 -ApplicationTypeName PingerApplicationType -ApplicationTypeVersion 1.0 -ApplicationParameter $p

```

## Internal Telemetry

Internal telemetry data is transmitted to Microsoft and contains information about YarpProxyApp. This information helps us track how many people are using the reverse proxy app as well as get a perspective on the app's retention rate. This data does not contain PII or any information about the services running in your cluster or the data handled by the applications. Nor do we capture the user application-specific configurations set for YarpProxyApp. 

**This information is only used by the Service Fabric team and will be retained for no more than 90 days. This telemetry is sent once every 24 hours** 

### Disabling / Enabling transmission of Internal Telemetry Data: 

Transmission of internal telemetry data is controlled by a setting and can be easily turned off. ```YarpProxyEnableTelemetry``` setting in ```ApplicationManifest.xml``` controls transmission of internal telemetry data. **Note that if you are deploying YarpProxyApp to a cluster running in a restricted region (China) or cloud (Gov) you should disable this feature before deploying to remain compliant. Please do not send data outside of any restricted boundary.**  

Setting the value to false as below will prevent the transmission of operational data: 

**\<Parameter Name="YarpProxyEnableTelemetry" DefaultValue="false" />** 

### Internal telemetry data details: 

Here is a full example of exactly what is sent in one of these telemetry events, in this case, from an SFRP cluster: 

```JSON
  {"EventName":"TelemetryEvent",
  "TaskName":"YarpProxy",
  "ClusterId":"00000000-1111-1111-0000-00f00d000d",
  "ClusterType":"SFRP",
  "Timestamp":"2022-03-08T00:00:16.2290850Z",
  "NodeNameHash":"3e83569d4c6aad78083cd081215dafc81e5218556b6a46cb8dd2b183ed0095ad"}
```

 We'll go through each object property in the JSON above.
-	**EventName** - this is the name of the telemetry event.
-	**TaskName** - this specifies that the event is from YarpProxyApp.
-	**ClusterId** - this is used to both uniquely identify a telemetry event and to correlate data that comes from a cluster.
-	**ClusterType** - this is the type of cluster: Standalone or SFRP.
-	**NodeNameHash** - this is a sha256 hash of the name of the Fabric node from where the data originates. It is used to correlate data from specific nodes in a cluster (the hashed node name will be known to be part of the cluster with a specific cluster id).
-	**Timestamp** - this is the time, in UTC, when YarpProxyApp sent the telemetry.

If the ClusterType is not SFRP then a TenantId (Guid) is sent for use in the same way we use ClusterId. 

This information will **really** help us so we greatly appreciate you sharing it with us!


## Tracing
Logs can be locally collected on every node that the app is running on. This is done via [Console Redirection](https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-deploy-existing-app#set-up-logging) which can be used to redirect console output  to a working directory. This provides the ability to verify that there are no errors during the setup or execution of the application in the Service Fabric cluster. Console redirection is disabled by default but can be enabled in ServiceManifest.xml for both FabricDiscovery and YarpProxy services. 

> WARNING: Never use the console redirection policy in an application that is deployed in production because this can affect the application failover. Only use this for local development and debugging purposes.


Since YarpProxyApp is an ASP.NET Core application it comes built in with various [logging capabilities](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-6.0). By default the logging providers that are supported included console, debug, eventsource and eventlog. We have also added the application insight logging provider so that logs can be collected outside the cluster. Just provide the application insight resource instrumentation key in the ApplicationManifest.xml.




# YARP Reverse Proxy for Service Fabric Integration

## Pre-reqs for development machine

* Windows 10 Version 1909 or later, x64
* .NET SDK (version indicated in global.json)
* .NET Core 5.x runtime (to run net5.0 tests)
* [Pre-reqs](#pre-reqs) above also apply regarding tls cert for local deployment

Dotnet sdks and runtimes can be downloaded from https://dotnet.microsoft.com/download .

## Building and Unit Testing

1. dotnet build dirs.proj
2. dotnet test dirs.proj
3. dotnet pack dirs.proj

For unit tests, you may want to filter out some tests. Refer to [the docs](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-vstest?tabs=netcore21) for information on how to use them. Example:

```cmd
dotnet test dirs.proj --filter HttpProxyTest
```

Alternatively, you can also open `YarpSF.sln` at the root of the repo with Visual Studio 2019.
Running builds and unit tests from VS2019 is supported (verified with Visual Studio 2019 16.10.2+ .NET 5 SDK version 5.0.201).

## Project Structure

This repo includes:

* `YarpProxyApp`: an example Service Fabric application, consisting of:
  * `YarpProxy.Service`: The runtime component that implements a Reverse Proxy using YARP. It reads configurations from remote service `FabricDiscovery.Service`
  * `FabricDiscovery.Service`: Responsible for discovering Service Fabric services that are configured to use YARP via Service Manifest Extensions, and exposes the summarized configurations for `YarpProxy.Service` to consume in real-time


## Running Locally

* Deploy `YarpProxyApp` to the local cluster
* Observe in Service Fabric Explorer that the application starts and all services are running without errors:

  ![Service Fabric Explorer](docs/sfx.png)

* Deploy the pinger test application mentioned in [Sample-Test-Application](#sample-test-application). Using a browser, access `http://localhost:8080/pinger0/PingerService`. If all works, you should get a `200 OK` response with contents resembling the following:

   ```json
   {
     "Pinger: I'm alive on ... "
   }
   ```

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft 
trademarks or logos is subject to and must follow 
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
