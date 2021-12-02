# ServiceFabricYarp 0.1.0-beta

The reverse proxy is an application, supplied out of band from the service fabric distribution, that customers deploy to their clusters and handles proxying traffic to backend services. The service, that potentially runs on every node in the cluster, takes care of handling endpoint resolution, automatic retry, and other connection failures on behalf of the clients. The reverse proxy can be configured to apply various policies as it handles requests from client services.

Using a reverse proxy allows the client service to use any client-side HTTP communication libraries and does not require special resolution and retry logic in the service. The reverse proxy is mostly a terminating endpoint for the TLS connections

>Note that, at this time, this is a reverse proxy built-in replacement and not a generic service fabric “gateway” able to handle partition queries, but that might be added (via customer written plugins or similar) in the future.

## Pre-reqs

* Windows 10 Version 1909 or later, x64
* .NET SDK (version indicated in global.json)
* .NET Core 5.x runtime (to run net5.0 tests)
* To setup certs, use `eng/Create-DevCerts.ps1`.
* To enable ACL to cert for Network Service user, use `eng/ACLCertificates.ps1`.

Dotnet sdks and runtimes can be downloaded from https://dotnet.microsoft.com/download .


## How it works 
As of this release, the services need to be explicitly exposed via [service extension labels](), enabling the proxying (HTTP) functionality for a particular service and endpoint. With the right labels’ setup, the reverse proxy will expose one or more endpoints on the local nodes for client services to use. The ports can then be exposed to the load balancer in order to get the services available outside of the cluster. The required certificates should be already deployed to the nodes where the proxy is running as is the case with any other Service Fabric application. For TLS scenario on remote SF cluster add appropriate ACL on cert, the proccess fabric.exe needs to be able to access the cert which runs as Network Service account. Also, certificate selection is done dynamically via SNI, therefore certificate needs to be stored under cert:\LocalMachine\My and binded with SF cluster's root domain name (i.e "sf-win-cluster.westus2.cloudapp.azure.com") in either the cert Subject Name and/or Subject Alternative Names

## Using the application  

You can clone the repo, build, and deploy or simply grab the latest [ZIP/SFPKG application](https://github.com/microsoft/service-fabric-traefik/releases/latest) from Releases section, modify configs, and deploy.

![alt text](/docs/yarp-cluster-view.png "Cluster View UI")

![alt text](/docs/yarp-service-view.png "Cluster Service View UI")


## Deploy it using PowerShell  

After either downloading the sfapp package from the releases or cloning the repo and building, you need to adjust the configuration settings to meet to your needs (this means changing settings in Settings.xml, ApplicationManifest.xml and any other changes needed).

>If you need a quick test cluster, you can deploy a test Service Fabric managed cluster following the instructions from here: [SFMC](https://docs.microsoft.com/en-us/azure/service-fabric/quickstart-managed-cluster-template), or via this template if you already have a client certificate and thumbprint available: [Deploy](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FAzure-Samples%2Fservice-fabric-cluster-templates%2Fmaster%2FSF-Managed-Basic-SKU-1-NT%2Fazuredeploy.json)

>Retrieve the cluster certificate TP using:  $serverThumbprint = (Get-AzResource -ResourceId /subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCEGROUP/providers/Microsoft.ServiceFabric/managedclusters/$CLUSTERNAME).Properties.clusterCertificateThumbprints

>Retrieve the client certificate TP using: $clientThumbprint = (Get-AzResource -ResourceId /subscriptions/$SUBSCRIPTION/resourceGroups/$RESOURCEGROUP/providers/Microsoft.ServiceFabric/managedclusters/$CLUSTERNAME).Properties.clients[0].thumbprint

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

Connect-ServiceFabricCluster -ConnectionEndpoint @('sf-win-cluster.westus2.cloudapp.azure.com:19000') -X509Credential -FindType FindByThumbprint -FindValue $clientThumbprint -StoreLocation LocalMachine -StoreName 'My' -ServerCertThumbprint $serverThumbprint

# Use this to remove a previous YarpProxy Application
#Remove-ServiceFabricApplication -ApplicationName fabric:/YarpProxyApp -Force
#Unregister-ServiceFabricApplicationType -ApplicationTypeName YarpProxyAppType -ApplicationTypeVersion 0.1.0-beta -Force

#Copy and register and run the YarpProxy Application
Copy-ServiceFabricApplicationPackage -CompressPackage -ApplicationPackagePath $appPath # -ApplicationPackagePathInImageStore YarpProxyApp
Register-ServiceFabricApplicationType -ApplicationPathInImageStore YarpProxyApp

#Fill the right values that are suitable for your cluster and application (the default ones below will work without modification if you used a Service Fabric managed cluster Quickstart template with one node type. Adjust the placement constraints to use other node types)
$p = @{
    YarpProxy_InstanceCount="1"
    YarpProxy_HttpPort="8080"
    YarpProxy_HttpsPort = "443"
    #YarpProxy_PlacementConstraints="NodeType == NT2"
}
$p

New-ServiceFabricApplication -ApplicationName fabric:/YarpProxyApp -ApplicationTypeName YarpProxyAppType -ApplicationTypeVersion 0.1.0-beta -ApplicationParameter $p


#OR if updating existing version:  

Start-ServiceFabricApplicationUpgrade -ApplicationName fabric:YarpProxyApp -ApplicationTypeVersion 0.1.0-beta -Monitored -FailureAction rollback
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

The only required label to expose a service via the reverse proxy is the **Yarp.Enable** set to true. Setting only this label will expose the service on a well known path and handle the basic scenarios.

```
http(s)://<Cluster FQDN | internal IP>:Port/ApplicationInstanceName/ServiceInstanceName?PartitionGuid=xxxxx
```

If you need to change the routes then you can add different labels configuring them.


## Supported Labels

Route section

* **Yarp.Routes.[routeName].Path**    Yarp rule to apply path prefix ['/id']. This rule is added on top of the default path generation. If this is set you **cannot** remove the prefix for the service to receive the stripped path. At the moment, there is no middleware/transform that allows you to strip the prefix. {**catch-all} path may be used to route all requests.

*Backend section*

* **Yarp.Backend.HealthCheck.Active.Enabled**          HealthCheck enabled ['true'/'false']
* **Yarp.Backend.Healthcheck.Path**        Healthcheck endpoint path ['/healtz']
* **Yarp.Backend.HealthCheck.Active.Timeout**    Healthcheck Timeout ['00:00:30']
* **Yarp.Backend.HealthCheck.Active.Interval**    Healthcheck interval ['00:00:10']


## Sample Test application

A sample test application, that is included in the release, can be deployed to test everything is working alright. After deployment, you should be able to reach it at:

https://your-cluster:8080/pinger0/PingerService/id


```Powershell

# Sample pinger app for validating (navidate to /pinger0/PingerService/id on https)
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
## Limitations
* *Path-based route matching is ONLY supported*
* *Basic HTTPS endpoint proxying includes ONLY TLS termination and no YARP endpoint server certificate validation*
* *No middleware/transform support to strip path prefix for the service to receive the stripped path*
* *No TCP proxying support ONLY HTTP*


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
