# ServiceFabricYarp 1.0.0


Table of Contents
=================
  * [Background](#background)
  * [Limitations](#limitations)
  * [How it works](#how-it-works)
  * [Pre-reqs](#pre-reqs)
  * [Using the Application](#using-the-application)
      * [Deploy it using PowerShell](#deploy-it-using-powershell)
      * [Add the right labels to your services](#add-the-right-labels-to-your-services)
      * [Supported Labels](#supported-labels)
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

>Note that, at this time, this is a reverse proxy built-in replacement and not a generic service fabric “gateway” able to handle partition queries, but that might be added (via customer written plugins or similar) in the future.

## Limitations
* YarpProxy app is only supported at the moment on Windows
* Path-based route matching is ONLY supported.
* Basic HTTPS endpoint proxying includes ONLY TLS termination and no YARP endpoint server certificate validation.
* No middleware/transform support to strip path prefix for the service to receive the stripped path.
* No TCP proxying support ONLY HTTP.


## How it works 
As of this release, the services need to be explicitly exposed via [service extension labels](#add-the-right-labels-to-your-services), enabling the proxying (HTTP) functionality for a particular service and endpoint. With the right labels’ setup, the reverse proxy will expose one or more endpoints on the local nodes for client services to use. The ports can then be exposed to the load balancer in order to get the services available outside of the cluster. The required certificates should be already deployed to the nodes where the proxy is running as is the case with any other Service Fabric application. For this preview, both http and https ports are being listened on by default, but for TLS to work on https port, need to satisfy the requirements regarding certificates mentioned in [Pre-reqs](#pre-reqs).

## Pre-reqs

* For TLS, certificate selection is done dynamically via SNI, therefore certificate needs to be created with the CN and DNS Names configured with the SF cluster's FQDN (i.e "sf-win-cluster.westus2.cloudapp.azure.com", "localhost", etc.) and provisioned under cert:\LocalMachine\My for each node were YarpProxy app is running. **Note:** If using self signed certificate or a non trusted certificate make sure it is placed in [Trusted Root Certification Authorities Certificate Store](https://docs.microsoft.com/en-us/windows-hardware/drivers/install/trusted-root-certification-authorities-certificate-store). Certificate selector does not consider non trusted certificates.
* To setup certs for local managed cluster, use `eng/Create-DevCerts.ps1`.
* To setup certs for remote managed cluster, this can be done using Azure Key Vault. Again, make sure the CN and DNS Names are configured with the SF cluster's FQDN. Next, need to [deploy the certificate to your cluster](https://docs.microsoft.com/en-us/azure/service-fabric/how-to-managed-cluster-application-secrets). Note: For TLS, if you get issues related to the certificates such as an error related to "System.Security.Authentication.AuthenticationException: The server mode SSL must use a certificate with the associated private key", try adding security permissions manually so that cluster can access the certificate using `eng/ACLCertificates.ps1`.


## Using the application

You can clone the repo, build, and deploy or simply grab the latest [ZIP/SFPKG application](https://github.com/microsoft/service-fabric-yarp/releases/latest) from Releases section, modify configs, and deploy.

![alt text](/docs/yarp-cluster-view.png "Cluster View UI")

![alt text](/docs/yarp-service-view.png "Cluster Service View UI")

## Deploy it using PowerShell  

After either downloading the sfapp package from the releases or cloning the repo and building, you need to adjust the configuration settings to meet to your needs (this means changing settings in Settings.xml, ApplicationManifest.xml and any other changes needed).

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
#Unregister-ServiceFabricApplicationType -ApplicationTypeName YarpProxyAppType -ApplicationTypeVersion 1.0.0 -Force

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

New-ServiceFabricApplication -ApplicationName fabric:/YarpProxyApp -ApplicationTypeName YarpProxyAppType -ApplicationTypeVersion 1.0.0 -ApplicationParameter $p


#OR if updating existing version:  

Start-ServiceFabricApplicationUpgrade -ApplicationName fabric:/YarpProxyApp -ApplicationTypeVersion 1.0.0 -ApplicationParameter $p -Monitored -FailureAction rollback 
```  

## Add the right labels to your services

### ServiceManifest file

This is a sample SF enabled service showing the currently supported labels. If the sf name is fabric:/pinger/PingerService, the endpoint will be exposed at that prefix: '/pinger/PingerService/'

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
            <Label Key='Yarp.Backend.Healthcheck.Path'>/</Label>
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

```
http(s)://<Cluster FQDN | internal IP>:Port/ApplicationInstanceName/ServiceInstanceName?PartitionGuid=xxxxx
```

If you need to change the routes then you can add different labels configuring them.


## Supported Labels

Route section

* **Yarp.Routes.[routeName].Path**    Yarp rule to apply path prefix ['/id']. This rule is added on top of the default path generation. If this is set, you **cannot** remove the prefix for the service to receive the stripped path. At the moment, there is no middleware/transform that allows you to strip the prefix. {**catch-all} path may be used to route all requests.

*Backend section*

* **Yarp.Backend.HealthCheck.Active.Enabled**          HealthCheck enabled ['true'/'false']
* **Yarp.Backend.Healthcheck.Path**        Healthcheck endpoint path ['/healtz']
* **Yarp.Backend.HealthCheck.Active.Timeout**    Healthcheck Timeout ['00:00:30']
* **Yarp.Backend.HealthCheck.Active.Interval**    Healthcheck interval ['00:00:10']


## Sample Test application

A sample test application, that is included in the release, can be deployed to test everything is working alright. After deployment, you should be able to reach it at:

https://your-cluster:8080/pinger0/PingerService/id


```Powershell

# Sample pinger app for validating (navigate to /pinger0/PingerService/id on https)
#Remove-ServiceFabricApplication -ApplicationName fabric:/pinger$i -Force
#Unregister-ServiceFabricApplicationType -ApplicationTypeName PingerApplicationType -ApplicationTypeVersion 1.0 -Force

$appPath = "C:\downloads\service-fabric-yarp\windows\pinger-yarp"

Copy-ServiceFabricApplicationPackage -CompressPackage -ApplicationPackagePath $appPath -ApplicationPackagePathInImageStore pinger-yarp
Register-ServiceFabricApplicationType -ApplicationPathInImageStore pinger-yarp

$p = @{
    "Pinger_Instance_Count"="3"
    "Pinger_Port"="7000"
    #"Pinger_PlacementConstraints"= "NodeType == NT2"
}

New-ServiceFabricApplication -ApplicationName fabric:/pinger0 -ApplicationTypeName PingerApplicationType -ApplicationTypeVersion 1.0 -ApplicationParameter $p

```

## Internal Telemetry

Internal telemetry data is transmitted to Microsoft and contains information about YarpProxyApp. This information helps us track how many people are using the reverse proxy app as well as get a perspectice on the app's retention rate. This data does not contain PII or any information about the services running in your cluster or the data handled by the applications. Nor do we capture the user application-specific configurations set for YarpProxyApp. 

**This information is only used by the Service Fabric team and will be retained for no more than 90 days. This telemetry is sent once every 24 hours** 

### Disabling / Enabling transmission of Internal Telemetry Data: 

Transmission of internal telemetry data is controlled by a setting and can be easily turned off. ```YarpProxyEnableTelemetry``` setting in ```ApplicationManifest.xml``` controls transmission of internal telemetry data. **Note that if you are deploying YarpProxyApp to a cluster running in a restricted region (China) or cloud (Gov) you should disable this feature before deploying to remain compliant. Please do not send data outside of any restricted boundary.**  

Setting the value to false as below will prevent the transmission of operational data: 

**\<Parameter Name="YarpProxyEnableTelemetry" DefaultValue="false" />** 

#### Internal telemetry data details: 

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
At the moment, logs can be locally collected on every node that the app is running on (e.g "C:\SfDevCluster\Data\_App\_Node_0\YarpProxyAppType_App0\log").

Since YarpProxyApp is an ASP.NET Core application it comes built in with various [logging capabilities](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-6.0). By default the logging providers that are supported included console, debug, eventsource and eventlog. We have also added the application insight logging provider so that logs can be collected outside the cluster. Just provide the Application Insight instrumentation key in the appsettings.json file under YarpProxy.Service. 




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

* Deploy the pinger test application mentioned in [Sample-Test-Application](#sample-test-application). Using a browser, access `https://localhost/pinger0/PingerService`. If all works, you should get a `200 OK` response with contents resembling the following:

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
