# Migrating from the Service Fabric Reverse Proxy to Service Fabric Yarp (SFYarp)

---

## Table of Contents

1. [Overview](#1-overview)
2. [SFYarp vs the built-in reverse proxy](#2-sfyarp-vs-the-built-in-reverse-proxy)
3. [Recommended migration path](#3-recommended-migration-path)
4. [Prerequisites](#4-prerequisites)
5. [Migration steps](#5-migration-steps)
6. [Client URL translation](#6-client-url-translation)
7. [Placement and sizing](#7-placement-and-sizing)
8. [TLS and certificate management](#8-tls-and-certificate-management)
9. [L7 gateway in front of SFYarp](#9-l7-gateway-in-front-of-sfyarp)
10. [Network lockdown (NSG and Service Tags)](#10-network-lockdown-nsg-and-service-tags)
11. [Limitations and behavior changes](#11-limitations-and-behavior-changes)
12. [Rollback plan](#12-rollback-plan)
13. [Troubleshooting](#13-troubleshooting)
14. [FAQ](#14-faq)
15. [Appendix — SFYarp labels reference](#appendix--sfyarp-labels-reference)

---

## 1. Overview

> **New SFMC customer?** If you're setting up SFYarp for the first
> time on a fresh Service Fabric managed cluster, start with the
> [README](../README.md) — it covers the product from a green-field
> onboarding angle. This guide is specifically for existing customers
> **migrating off the built-in reverse proxy**.

This migration guide walks you through replacing the built-in Service
Fabric Reverse Proxy with Service Fabric Yarp (SFYarp) — prerequisites,
how the two solutions compare, the recommended path, and the
migration-specific concerns you may hit along the way. For general
SFYarp product topics referenced from this guide — full TLS/cert
setup, ARM deployment shape, L7 gateway configuration, NSG rules,
placement/sizing, log collection, and the full label catalog — the
[README](../README.md) is the canonical source.

It applies if you run one or more Service Fabric
applications behind the built-in reverse proxy (the Service Fabric
HTTP reverse-proxy component that listens on the "reverse proxy endpoint",
commonly port `19081`) and need a replacement. Two situations
typically drive this migration:

- **You are moving to a Service Fabric managed cluster (SFMC).** The
  built-in reverse proxy does not exist on SFMC. **SFYarp is the
  recommended reverse-proxy option on SFMC.**
- **You want richer ingress capabilities than the built-in reverse
  proxy provides.** SFYarp is built on
  [Microsoft YARP](https://github.com/dotnet/yarp) and
  offers capabilities customers commonly ask for — rule-based
  routing, configurable active health probes, SNI-based TLS with
  per-service certificates, header transforms, and other capabilities.

SFYarp ships as two ASP.NET Core services that together proxy inbound
HTTP/HTTPS traffic to your Service Fabric services:

- **`YarpProxy.Service`** — the proxy itself, built on
  [Microsoft YARP](https://github.com/dotnet/yarp). It
  terminates client connections and forwards requests to backends.
- **`FabricDiscovery.Service`** — the Service Fabric integration
  layer. It watches the cluster for services, endpoints, and SFYarp
  labels, and publishes the resulting routing configuration to
  `YarpProxy.Service`.

Because SFYarp is packaged as an ordinary SF application, the same
package runs on classic Service Fabric Resource Provider (SFRP)
clusters and on SFMC. This guide targets **SFYarp 2.0.0 or later**.
It requires Service Fabric **11.5 or later** and is **Windows-only**
today (no TCP proxying, no Linux support).

**Deployment scope.** SFYarp deploys as a single SF application per
cluster, running one instance on each node of your ingress node type
(`InstanceCount = -1`). It is not a per-application component — the
same SFYarp application fronts every labeled service in the cluster.

**Key behavior changes to plan for:**

1. **Per-service label configuration.** For every service you want
   SFYarp to expose, add `Yarp.Enable=true` — plus any additional
   `Yarp.*` labels for routing, health checks, transforms, or load
   balancing — in its `ServiceManifest.xml`. See
   [§5.3](#53-opt-in-a-service-with-labels).
2. **Partition addressing changes.** `PartitionKind=` + `PartitionKey=`
   becomes `PartitionID=<guid>`. Replica-role and named-listener move
   from per-request query params to per-service labels — see
   [§6](#6-client-url-translation).
3. **TLS is Kestrel + SNI.** Not http.sys with a single cluster certificate —
   see [§8](#8-tls-and-certificate-management).

**Migration path at a glance:**

1. Deploy SFYarp on your existing SFRP cluster on non-conflicting
   ports and cut traffic to it while the built-in reverse proxy stays
   on as a safety net.
2. Turn off the built-in reverse proxy in the cluster manifest.
3. If moving to SFMC, migrate the cluster. The same SFYarp package
   you validated on SFRP redeploys unchanged.

---

## 2. SFYarp vs the built-in reverse proxy

The three behavior changes that shape most migrations are already
summarized in [§1](#1-overview). The table below is the
subset of dimensions that force a decision during migration
planning. Feature-level differences (session affinity, load-balancing
policies, transforms, CORS, active health checks, etc.) live in the
SFYarp [README](https://github.com/microsoft/service-fabric-yarp#readme).

| Migration decision | Built-in Reverse Proxy | SFYarp |
|---|---|---|
| Runs on SFMC | No | Yes |
| Minimum SF version | Bundled with runtime | 11.5 |
| Service opt-in | Implicit (every service exposed) | Explicit per service via `Yarp.Enable=true` |
| Partition addressing | `PartitionKind` + `PartitionKey` | `PartitionID=<guid>` |
| Replica / listener targeting | Per-request query params | Per-service labels |
| TLS termination | http.sys, single cluster certificate | Kestrel + SNI, multiple certificates |
| Config-change model | Cluster-manifest upgrade (cluster-wide) | Per-application upgrade |
| Failure blast radius | Cluster-wide | Application-scoped |
| Rollback | Cluster-manifest revert | ARM `version` flip to the previous `applicationTypes/versions` (or `Start-ServiceFabricApplicationUpgradeRollback` for an in-flight rollout) |

**Client action items before you start.** Grep client code and SDK
usage for the strings `PartitionKind`, `PartitionKey`,
`TargetReplicaSelector`, `ListenerName`, `X-ServiceFabric`, and
`Timeout=`. See [§6](#6-client-url-translation) for what each one
becomes on SFYarp.

---

## 3. Recommended migration path

Run this in three separate change windows so each step makes exactly
one change and is independently reversible.

```
    +----------------------------------+
    |  SFRP + built-in reverse proxy   |
    |  (baseline you have now)         |
    +----------------------------------+
                 |
                 |  Step 1: Deploy SFYarp on non-conflicting ports.
                 |          Keep the built-in reverse proxy on and cut client traffic gradually.
                 v
    +----------------------------------+
    |  SFRP + SFYarp                   |
    |  built-in reverse proxy still on |
    +----------------------------------+
                 |
                 |  Step 2: Disable the built-in reverse proxy in the cluster manifest.
                 |          Cluster is otherwise unchanged.
                 v
    +----------------------------------+
    |  SFRP + SFYarp only              |
    +----------------------------------+
                 |
                 |  Step 3: Migrate the cluster to SFMC.
                 |          The SFYarp package redeploys unchanged.
                 v
    +----------------------------------+
    |  SFMC + SFYarp                   |
    +----------------------------------+
```

Why this ordering matters:

- **Step 1** validates SFYarp against your real routes, real TLS certificates,
  real backends, and real client SDKs — while the built-in reverse
  proxy is still live. If you find a routing gap, an authentication
  mismatch, or a header your client depended on, you cut traffic back
  with a DNS or load-balancer flip.
- **Step 2** removes the built-in reverse proxy from the cluster, but the
  cluster itself is unchanged. Any client regression here is easy to
  correlate ("the built-in reverse proxy went away") without mixing it with the
  SFMC move.
- **Step 3** is the ARM-level cluster migration to SFMC. Because the
  reverse proxy is already the same package you validated in Steps 1
  and 2, the SFMC move touches only the cluster resource and nodes —
  not ingress.

> **Do not combine Steps 1–3 into a single change window.** Every gap
> listed in [§11](#11-limitations-and-behavior-changes) is easier to
> diagnose when only one thing has changed.

---

## 4. Prerequisites

Before you deploy SFYarp anywhere:

- [ ] **Platform:** Service Fabric **11.5 or later** on **Windows**.
- [ ] **SFYarp package:** **SFYarp 2.0.0 or later**. This release is
      self-contained — the .NET 10 ASP.NET Core runtime ships inside
      the application package, so no separate runtime install is
      required.
- [ ] **Service inventory:** every service currently reachable through
      the built-in reverse proxy — see
      [§4.1](#41-inventory-current-services).
- [ ] **Certificates:** a certificate whose SAN covers every hostname callers
      use, plus a plan to land it on ingress nodes — see
      [§8](#8-tls-and-certificate-management).
- [ ] **Client-code audit:** grep callers for `PartitionKind`,
      `PartitionKey`, `TargetReplicaSelector`, `ListenerName`,
      `X-ServiceFabric`, and `Timeout=` — see
      [§6](#6-client-url-translation).
- [ ] **Ingress topology:** whether an L7 gateway (Azure Application
      Gateway, Azure Front Door, NGINX, etc.) will sit in front of
      SFYarp — see [§9](#9-l7-gateway-in-front-of-sfyarp).
- [ ] **Capacity baseline:** plan a load test against SFYarp on your
      target VM SKU before cutting traffic — see
      [§7](#7-placement-and-sizing).
- [ ] **Ports:** which ports SFYarp will bind. Shipped defaults are
      `8080` (HTTP) and `443` (HTTPS). Keep SFYarp's ports **distinct
      from the built-in reverse proxy's port** (typically `19081`) so
      the two proxies can run side-by-side during migration. If your
      cluster already binds `443` for other ingress, pick a non-443
      HTTPS port (for example `8443`) until cutover — see
      [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster).
- [ ] **Rollback path:** confirm the L7 backend-pool edit that reverts
      traffic to the built-in reverse proxy — see [§12](#12-rollback-plan).

> **Ownership shifts to your team.** The built-in reverse proxy was a
> platform component maintained by the SF runtime. SFYarp is an SF
> application your team owns: upgrade cadence, alerts, capacity, and
> rollouts are yours to run.

### 4.1 Inventory current services

The built-in reverse proxy exposes **every** Service Fabric service on
nodes that have the reverse-proxy port open. There is no allow-list. To
build your migration inventory, run this against the cluster:

```powershell
Connect-ServiceFabricCluster ...

Get-ServiceFabricApplication | ForEach-Object {
  $app = $_.ApplicationName.OriginalString
  Get-ServiceFabricService -ApplicationName $_.ApplicationName | ForEach-Object {
    $parts = Get-ServiceFabricPartition -ServiceName $_.ServiceName
    [pscustomobject]@{
      Application     = $app
      Service         = $_.ServiceName.OriginalString
      ServiceTypeName = $_.ServiceTypeName
      ServiceKind     = $_.ServiceKind
      PartitionKind   = ($parts | Select-Object -First 1).PartitionInformation.Kind
      PartitionCount  = ($parts | Measure-Object).Count
    }
  }
} | Export-Csv sf-services.csv -NoTypeInformation
```

Treat this CSV as your migration tracker — add `YarpLabels` (planned)
and `MigratedOn` columns as you work through
[§5.3](#53-opt-in-a-service-with-labels).

For every row, decide:

- **Should it be reachable via ingress?** If not, the service will be
  correctly kept private by SFYarp until you opt it in.
- **What partition scheme does it use?** Anything not `Singleton` needs
  its callers to switch from `PartitionKind`+`PartitionKey` to
  `PartitionID=<guid>` — see [§6](#6-client-url-translation).
- **Does it need a specific listener or replica role?** Check
  `ServiceManifest.xml` `<Endpoints>` for named listeners. Those
  become per-service labels — see [§5.3](#53-opt-in-a-service-with-labels).
- **What labels will it need?** Beyond `Yarp.Enable=true`, plan the
  routes, active health checks, and any request/response transforms
  the service needs. See the
  [Appendix](#appendix--sfyarp-labels-reference) for the label set
  typically used during migration.

---

## 5. Migration steps

### 5.1 Deploy SFYarp on your SFRP cluster

1. **Get the package.** SFYarp 2.x ships as a signed application
   package. Download `service-fabric-yarp.zip` from the
   [GitHub Releases page](https://github.com/microsoft/service-fabric-yarp/releases)
   and extract it. You end up with a `YarpProxyApp` folder
   containing the ApplicationPackageRoot layout. The rest of this
   guide operates on that folder.
2. **Decide the ports.** SFYarp's shipped defaults are
   `YarpProxy_HttpPort=8080` and `YarpProxy_HttpsPort=443`. Two
   constraints during migration:

   - Don't collide with the built-in reverse proxy's port (commonly
     `19081`).
   - Don't collide with anything else already bound to `443` on the
     ingress node type.

   For coexistence with the built-in reverse proxy or other `443`
   ingress, override `YarpProxy_HttpsPort` to a non-conflicting value
   (for example `8443`) while cutting over. Switch back to `443`
   after [§5.5](#55-turn-off-the-built-in-reverse-proxy) turns off
   the built-in reverse proxy.

   > **In production, prefer HTTPS only.** Omit the HTTP port from the
   > LB/NSG in step 4.
3. **Deploy the SFYarp application via ARM.** Author the SFYarp
   application in the same ARM / Bicep template that provisions the
   cluster — see [§5.2](#52-deploy-sfyarp-via-arm-recommended) for
   the resource shape and rotation flow. A PowerShell publish path
   for dev/test iteration on an already-running cluster is available
   at the end of §5.2.

4. **Open the SFYarp port on the load balancer and NSG.** Open the
   port on **both** the load balancer and the NSG: callers cannot
   reach SFYarp until both are in place.

   - **Classic SFRP.** Add a `loadBalancingRules` entry (frontend port
     → VMSS backend pool → TCP probe) to your public load balancer,
     add the port to the node type's `frontendPorts`, and add an
     inbound `Allow` rule to the VMSS's NSG.
   - **SFMC.** Add the rule to the `Microsoft.ServiceFabric/managedclusters`
     resource, **not** the underlying VMSS (rules added at the VMSS
     level get reverted by SFMC reconciliation). Both
     `loadBalancingRules` and `networkSecurityRules` live under the
     managed cluster resource's `properties`.

   Minimal SFMC snippet for HTTPS on 8443 (merge under the
   `Microsoft.ServiceFabric/managedclusters` resource):

   ```json
   {
     "type":       "Microsoft.ServiceFabric/managedclusters",
     "apiVersion": "2024-04-01",
     "name":       "[parameters('clusterName')]",
     "properties": {
       "loadBalancingRules": [
         {
           "frontendPort":   8443,
           "backendPort":    8443,
           "protocol":       "tcp",
           "probeProtocol":  "tcp",
           "probePort":      8443
         }
       ]
     }
   }
   ```

   See [§10](#10-network-lockdown-nsg-and-service-tags) for the
   NSG rule that restricts the source.

5. **Smoke test.** Label one non-critical service (see
   [§5.3](#53-opt-in-a-service-with-labels)) and send a request to
   SFYarp's port from a test machine. Confirm it lands on the
   intended backend. This validates the end-to-end deployment before
   the gradual production cutover in
   [§5.4](#54-cut-over-client-traffic-to-sfyarp).

### 5.2 Deploy SFYarp via ARM (recommended)

Author the SFYarp application in the same ARM / Bicep template that
provisions the cluster so app rollouts happen through the standard
CI/CD flow with the rest of your infrastructure. This is the recommended path for both
SFRP and SFMC production.

**1. Repackage the shipped package as an SFPKG.** Start from the
`YarpProxyApp` folder you extracted in [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster)
step 1. ARM's `applicationTypes/versions.appPackageUrl` needs a URL to
a zip-shaped `.sfpkg` — zip the folder and give it the `.sfpkg`
extension:

> **Edit `ApplicationManifest.xml` before you zip.** If you need to
> declare an `<EndpointCertificate>` for HTTPS (see
> [README §Private key ACLs](../README.md#private-key-acls-endpointcertificate-declaration)),
> add a `<ServicePackageResourceGovernancePolicy>` for CPU/memory limits
> (see [§7](#7-placement-and-sizing)), tune placement via the
> shipped `YarpProxy_PlacementConstraints` parameter (see
> [§7](#7-placement-and-sizing)), add extra `<Parameters>` you plan
> to override from ARM, or apply any `<ConfigOverride>` blocks, do
> it now in `YarpProxyApp\ApplicationManifest.xml` inside the
> extracted folder.

```powershell
$src = '.\YarpProxyApp'   # folder extracted from service-fabric-yarp.zip

Compress-Archive -Path "$src\*" -DestinationPath 'out\YarpProxyApp.zip'
Move-Item 'out\YarpProxyApp.zip' 'out\YarpProxyApp.sfpkg'
```

**2. Upload the SFPKG somewhere the cluster can reach it.** Typically
an Azure Storage blob you control, with the **version in the blob
path** (e.g. `sfyarp-packages/2.0.0/YarpProxyApp.sfpkg`) so previous
versions remain available for rollback.

> **Application name is fixed.** Deploy the application as
> `fabric:/YarpProxyApp` exactly. `YarpProxy.Service` uses this
> name to discover `FabricDiscovery.Service` in the cluster and it
> can't be overridden through configuration. Any other name causes
> `YarpProxy.Service` to crash-loop shortly after activation (see
> [§13.2](#132-common-migration-symptoms-and-fixes)).

**3. Reference the SFPKG from ARM.** Add two resources under
`Microsoft.ServiceFabric/managedclusters` (a complete working
example lives at [`docs/samples/sfmc-arm/cluster.json`](samples/sfmc-arm/cluster.json)):

```json
[
  {
    "type":       "Microsoft.ServiceFabric/managedclusters/applicationTypes",
    "apiVersion": "2024-04-01",
    "name":       "[concat(parameters('clusterName'), '/YarpProxyAppType')]",
    "location":   "[parameters('location')]",
    "dependsOn":  [
      "[resourceId('Microsoft.ServiceFabric/managedclusters', parameters('clusterName'))]"
    ],
    "properties": {}
  },
  {
    "type":       "Microsoft.ServiceFabric/managedclusters/applicationTypes/versions",
    "apiVersion": "2024-04-01",
    "name":       "[concat(parameters('clusterName'), '/YarpProxyAppType/2.0.0')]",
    "location":   "[parameters('location')]",
    "dependsOn":  [
      "[resourceId('Microsoft.ServiceFabric/managedclusters/applicationTypes', parameters('clusterName'), 'YarpProxyAppType')]"
    ],
    "properties": {
      "appPackageUrl": "[parameters('yarpProxyAppPackageUrl')]"
    }
  },
  {
    "type":       "Microsoft.ServiceFabric/managedclusters/applications",
    "apiVersion": "2024-04-01",
    "name":       "[concat(parameters('clusterName'), '/YarpProxyApp')]",
    "location":   "[parameters('location')]",
    "dependsOn":  [
      "[resourceId('Microsoft.ServiceFabric/managedclusters/applicationTypes/versions', parameters('clusterName'), 'YarpProxyAppType', '2.0.0')]"
    ],
    "properties": {
      "version": "[resourceId('Microsoft.ServiceFabric/managedclusters/applicationTypes/versions', parameters('clusterName'), 'YarpProxyAppType', '2.0.0')]",
      "parameters": {
        "YarpProxy_InstanceCount":        "-1",
        "YarpProxy_PlacementConstraints": "NodeType==FrontEnd",
        "YarpProxy_HttpPort":             "8080",
        "YarpProxy_HttpsPort":            "8443",
        "FabricDiscovery_InstanceCount":  "-1",
        "YarpProxyEnableTelemetry":       "true"
      }
    }
  }
]
```

Notes:

- The application-type **name** (`YarpProxyAppType`) matches the type
  name in the shipped `ApplicationManifest.xml`. Do **not** rename it.
- The version segment (`2.0.0`) matches the manifest version the
  release pipeline stamped in the shipped package.
- `YarpProxy_InstanceCount = "-1"` means "one instance per eligible
  node". `YarpProxy_PlacementConstraints` limits eligibility to the
  node types that should carry ingress. Replace `FrontEnd` with your
  ingress node type. See [§7](#7-placement-and-sizing).
  `FabricDiscovery.Service` reuses `YarpProxy_PlacementConstraints`
  from the shipped manifest. No separate parameter is exposed. For
  HTTPS cert-selector parameters, see
  [§8](#8-tls-and-certificate-management).
- If your cluster template already provisions the node type with the
  KV VM extension (see
  [README §Deploying certificates to nodes](../README.md#deploying-certificates-to-nodes)),
  no additional certificate plumbing is needed here. The SFYarp
  application will find the certificate in `LocalMachine\My` at
  activation time.

**4. Upgrade to a new SFYarp version.** Fetch the new package version
per [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster) step 1, then
repeat steps 1–3 above: upload the new SFPKG to a **new**
version-in-path blob (don't overwrite, because SFMC rejects in-place
`appPackageUrl` changes on an active version resource). Add a new
`applicationTypes/versions` resource pointing at it, then flip the
`applications` resource's `version` property to the new resource ID.
SFMC performs a monitored rolling upgrade honoring the cluster's
`upgradePolicy` — see
[Service Fabric application upgrade](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-upgrade)
and
[Application upgrade parameters](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-upgrade-parameters)
for the full parameter surface. Rollback is the same `version` flip
back to the previous resource ID, which is why version-in-path
storage matters. Keep the previous SFPKG and its
`applicationTypes/versions` resource around for at least one
rollback window.

<details>
<summary><strong>PowerShell publish (dev/test only)</strong></summary>

For dev/test iteration on an already-running cluster you can publish
the application from PowerShell instead of ARM. Do **not** use this
for production. Production rollouts should go through the ARM path
above so the deployment matches the rest of your infrastructure.

```powershell
Connect-ServiceFabricCluster ...

$pkg = "C:\path\to\extracted\YarpProxyApp"
Copy-ServiceFabricApplicationPackage `
    -ApplicationPackagePath $pkg `
    -ApplicationPackagePathInImageStore YarpProxyApp `
    -TimeoutSec 1800
Register-ServiceFabricApplicationType `
    -ApplicationPathInImageStore YarpProxyApp
New-ServiceFabricApplication `
    -ApplicationName fabric:/YarpProxyApp `
    -ApplicationTypeName YarpProxyAppType `
    -ApplicationTypeVersion "<version-from-release>" `
    -ApplicationParameter @{
        YarpProxy_InstanceCount        = "-1"
        YarpProxy_PlacementConstraints = "NodeType==FrontEnd"
        YarpProxy_HttpPort             = "8080"
        YarpProxy_HttpsPort            = "8443"   # coexistence port. Switch to 443 after §5.5
        FabricDiscovery_InstanceCount  = "-1"
        YarpProxyEnableTelemetry       = "true"
    }
```

Verify:

```powershell
Get-ServiceFabricApplication -ApplicationName fabric:/YarpProxyApp |
  Get-ServiceFabricService | Get-ServiceFabricPartition |
  Get-ServiceFabricReplica |
  Format-Table NodeName, ReplicaOrInstanceStatus
```

</details>

### 5.3 Opt in a service with labels

This is the biggest behavioral change from the built-in reverse proxy.
**Every** service you want SFYarp to expose must add an `<Extensions>`
block to its `<StatelessServiceType>` or `<StatefulServiceType>` in
`ServiceManifest.xml`. A minimal example that catches everything and
enables active health checks against `/`:

```xml
<StatelessServiceType ServiceTypeName="PingerServiceType" UseImplicitHost="true">
  <Extensions>
    <Extension Name="Yarp">
      <Labels xmlns="http://schemas.microsoft.com/2015/03/fabact-no-schema">
        <Label Key="Yarp.Enable">true</Label>
        <Label Key="Yarp.Routes.defaultRoute.Path">/{**catchall}</Label>
        <Label Key="Yarp.Backend.HealthCheck.Active.Enabled">true</Label>
        <Label Key="Yarp.Backend.HealthCheck.Active.Path">/</Label>
        <Label Key="Yarp.Backend.HealthCheck.Active.Interval">00:00:30</Label>
        <Label Key="Yarp.Backend.HealthCheck.Active.Timeout">00:00:05</Label>
      </Labels>
    </Extension>
  </Extensions>
</StatelessServiceType>
```

Common recipes:

- **Match by hostname:**
  ```xml
  <Label Key="Yarp.Routes.tenantA.Path">/{**catchall}</Label>
  <Label Key="Yarp.Routes.tenantA.Hosts">tenant-a.contoso.com</Label>
  ```
- **Match by method + header:**
  ```xml
  <Label Key="Yarp.Routes.writes.Path">/api/{**catchall}</Label>
  <Label Key="Yarp.Routes.writes.Methods">POST,PUT,DELETE</Label>
  <Label Key="Yarp.Routes.writes.MatchHeaders.[0].Name">x-tenant</Label>
  <Label Key="Yarp.Routes.writes.MatchHeaders.[0].Values">acme</Label>
  ```
- **HTTPS backend, TLS 1.2/1.3, HTTP/2:**
  ```xml
  <Label Key="Yarp.Backend.HttpRequest.Version">2</Label>
  <Label Key="Yarp.Backend.HttpClient.SslProtocols">Tls12,Tls13</Label>
  <Label Key="Yarp.Backend.HttpClient.DangerousAcceptAnyServerCertificate">false</Label>
  ```
- **Reject HTTP-only backends** (recommended for production):
  ```xml
  <Label Key="Yarp.Backend.AllowInsecureHttp">false</Label>
  ```
  In current SFYarp releases the default is `true` — SFYarp will forward
  to a plain-HTTP backend unless the route sets this label to
  `false`.
- **Stateful primary-only:**
  ```xml
  <Label Key="Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode">PrimaryOnly</Label>
  ```
- **Alternate listener** (when the service exposes multiple named
  endpoints):
  ```xml
  <Label Key="Yarp.Backend.ServiceFabric.ListenerName">MyAltListener</Label>
  ```
- **Preserve the original `Host` header** to the backend (needed for
  backend SNI validation — see
  [README §In-cluster service-to-service traffic](../README.md#in-cluster-service-to-service-traffic)):
  ```xml
  <Label Key="Yarp.Routes.defaultRoute.Transforms.[0].RequestHeaderOriginalHost">true</Label>
  ```

> **Containers.** Containerized SF services need no SFYarp-specific
> configuration. Opt them in with the same `ServiceManifest.xml`
> labels as any other service. SFYarp routes to whichever endpoint
> SF publishes in the Naming Service.

### 5.4 Cut over client traffic to SFYarp

> **`Yarp.Enable=true` is additive.** Opting a service in makes it
> reachable via SFYarp but does not remove it from the built-in
> reverse proxy. Both paths serve the service until you turn off the
> built-in reverse proxy cluster-wide in [§5.5](#55-turn-off-the-built-in-reverse-proxy).

Once SFYarp is running and at least one service is labeled, roll it in
behind your existing ingress in this order to avoid dropped traffic:

1. **Verify SFYarp is healthy** from a node-local `curl` against
   `/proxy-health` (or whichever route you've reserved for the L7
   probe — see [README §L7 gateway integration](../README.md#l7-gateway-integration)) before touching any
   ingress config.
2. **Add SFYarp to the L7 backend pool** (Azure Application Gateway,
   Azure Front Door, NGINX, or any other L7 sitting in front) as a
   *second* backend alongside the built-in reverse proxy. Both proxies
   receive traffic: SFYarp handles whatever it has labels for, and the
   built-in reverse proxy handles the rest.
3. **Opt in services one at a time** (see [§5.3](#53-opt-in-a-service-with-labels)).
   Verify each service works through SFYarp before opting in the
   next. This is the point where you catch label misconfigurations,
   HTTPS-vs-HTTP mismatches, and route conflicts.
4. **Drain the built-in reverse proxy** by removing it from the L7
   backend pool once all opted-in services are stable. Keep it
   running on the cluster for a rollback window.
5. **Turn off the built-in reverse proxy** ([§5.5](#55-turn-off-the-built-in-reverse-proxy))
   only after the rollback window passes with no incidents.

Do **not** put both proxies behind the same LB pool without an L7 in
front doing path-based selection. Clients will hit either proxy at
random, and response semantics differ. Behavior will be inconsistent.

> **No L7 in front?** If clients hit the LB directly, cut over by
> pointing the LB frontend port at SFYarp's backend port instead of
> the built-in reverse proxy's: a single LB rule swap. Rollback is the
> reverse swap. Both proxies keep running on their own ports during
> the transition.

### 5.5 Turn off the built-in reverse proxy

> **SFMC skip.** This section only applies on SFRP. SFMC never shipped
> the built-in reverse proxy — there is nothing to turn off. If you
> are on SFMC, continue to [§5.6](#56-move-the-cluster-to-sfmc).

After a full soak with SFYarp handling 100% of traffic:

1. In the cluster manifest, set:
   ```xml
   <Section Name="ApplicationGateway/Http">
     <Parameter Name="IsEnabled" Value="false" />
   </Section>
   ```
2. Remove the reverse-proxy endpoint (`HttpApplicationGatewayEndpoint`
   / `ReverseProxyEndpoint`) from the cluster manifest. This frees the
   port and stops the built-in reverse proxy from starting on new nodes.
3. Remove `reverseProxyCertificate` (or
   `reverseProxyCertificateCommonNames`) from the
   `Microsoft.ServiceFabric/clusters` resource properties. Leaving it
   after `IsEnabled=false` produces cluster-manifest drift and keeps
   the SF extension provisioning a certificate store that no listener
   consumes.
4. Update NSG rules and LB rules so the old port is no longer routed.
5. Apply the cluster-manifest upgrade (SFRP: ARM template update).
6. If you had been running SFYarp on non-standard ports (e.g. 8443 for
   coexistence), you can now switch it to 443 by updating the
   `applications` resource in ARM with `YarpProxy_HttpsPort="443"`
   and redeploying (see [§5.2](#52-deploy-sfyarp-via-arm-recommended)
   step 4).

### 5.6 Move the cluster to SFMC

Perform your normal SFRP → SFMC migration. Because the SFYarp package
is already validated on SFRP:

- Author the SFYarp application in the same ARM / Bicep template that
  provisions the managed cluster — see
  [§5.2](#52-deploy-sfyarp-via-arm-recommended). Since §5.2 is
  already how you deployed SFYarp on SFRP, the SFMC deployment shares
  the same template shape.
- Labels travel with the application package. No re-labeling needed
  on SFMC.
- **Cut traffic from the SFRP cluster to the SFMC cluster** at your
  DNS or global load balancer. This is a cluster-level ingress swap,
  not the proxy-level swap covered in
  [§5.4](#54-cut-over-client-traffic-to-sfyarp) — SFYarp is already
  the reverse proxy on both clusters. Shift traffic gradually and
  keep the SFRP cluster running for a rollback window.

---

## 6. Client URL translation

> **Path-prefix note.** SFYarp preserves the built-in reverse proxy's
> `/{AppName}/{ServiceName}/…` path prefix (see the base-URL columns
> in the tables below). Only the query-string contract changes. Calls
> to bare backend paths (for example `/hello` instead of
> `/PingerApp/PingerService/hello`) return 404. See
> [§13.2](#132-common-migration-symptoms-and-fixes).

> The built-in reverse proxy's URL contract is documented on
> [Microsoft Learn](https://learn.microsoft.com/en-us/azure/service-fabric/service-fabric-reverseproxy#uri-format-for-addressing-services-by-using-the-reverse-proxy).
> The tables below map each built-in query-string parameter to its
> SFYarp equivalent.

### 6.1 Stateless (Singleton)

| Aspect | Built-in | SFYarp |
|---|---|---|
| Base URL | `https://cluster:19081/App1/Api/orders` | `https://cluster:8443/App1/Api/orders` |

### 6.2 Stateful (Int64 or Named)

| Aspect | Built-in | SFYarp |
|---|---|---|
| Base URL | `https://cluster:19081/App1/Api/orders?PartitionKind=Int64Range&PartitionKey=5` | `https://cluster:8443/App1/Api/orders?PartitionID=<guid-of-partition-5>` |
| Partition GUID discovery | Not needed — the proxy resolves it | Client resolves once with `Get-ServiceFabricPartition` (or the equivalent SDK call) and caches the GUID per key |
| Target replica role | Per-request `&TargetReplicaSelector=`. Default is `PrimaryReplica` | Per-service label `Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode`. Default is `PrimaryOnly` |

**Resolving the partition GUID from client code.** For most stateful
callers this is the largest client-side change of the migration. Do it
once per `(service, key)`, cache the result, and refresh only when the
service is repartitioned (rare). Minimal C# example:

```csharp
using System;
using System.Fabric;
using System.Fabric.Query;
using System.Threading.Tasks;

var client = new FabricClient();

// Named partition ("pk-acme", "pk-contoso", ...):
async Task<Guid> ResolveNamedAsync(Uri serviceName, string partitionName)
{
    ServicePartitionList partitions =
        await client.QueryManager.GetPartitionListAsync(serviceName);

    foreach (Partition partition in partitions)
    {
        var named = partition.PartitionInformation as NamedPartitionInformation;
        if (named != null && named.Name == partitionName)
        {
            return named.Id;
        }
    }
    throw new InvalidOperationException($"No partition named '{partitionName}'.");
}

// Int64Range partition (hash your key to a long, find the covering range):
async Task<Guid> ResolveInt64Async(Uri serviceName, long key)
{
    ServicePartitionList partitions =
        await client.QueryManager.GetPartitionListAsync(serviceName);

    foreach (Partition partition in partitions)
    {
        var range = partition.PartitionInformation as Int64RangePartitionInformation;
        if (range != null && key >= range.LowKey && key <= range.HighKey)
        {
            return range.Id;
        }
    }
    throw new InvalidOperationException($"No partition covering key {key}.");
}
```

The returned GUID feeds directly into `?PartitionID=<guid>` on the
SFYarp URL.

**Replica-selector value mapping:**

| Built-in `TargetReplicaSelector` | SFYarp `StatefulReplicaSelectionMode` |
|---|---|
| `PrimaryReplica` (default) | `PrimaryOnly` (default) |
| `RandomSecondaryReplica` | `SecondaryOnly` |
| `RandomReplica` | `All` |

SFYarp's replica selection is per-service, not per-request. A backend
that needs to serve both primary and secondary reads must expose two
SF service names — one labeled `PrimaryOnly`, one `SecondaryOnly` —
or accept default primary routing.

### 6.3 Named listener

| Aspect | Built-in | SFYarp |
|---|---|---|
| Select non-default endpoint | `?ListenerName=AltHttps` | Label `Yarp.Backend.ServiceFabric.ListenerName=AltHttps` |

### 6.4 Per-request timeout

| Aspect | Built-in | SFYarp |
|---|---|---|
| Per-request override | `?Timeout=30` (seconds). Default is 120 seconds | Not per request. Set the label `Yarp.Backend.HttpRequest.ActivityTimeout=00:00:30` on the target service |

Callers that never set `Timeout=` explicitly relied on the built-in
reverse proxy's 120-second default. Set an explicit `ActivityTimeout`
label on migrated services if that timeout mattered to your workload.
The YARP default is shorter (100 seconds).

### 6.5 Response headers

The built-in reverse proxy emits `X-ServiceFabric: NoRetry` on failures the
gateway won't retry, and `X-ServiceFabric: ResourceNotFound` on
name-resolution misses. SFYarp emits **neither**. Clients that switch
on these must be updated to look at HTTP status code and body only.

---

## 7. Placement and sizing

Position SFYarp on the same node type(s) that already carry your
ingress-facing services on the SFRP cluster today (same fault
domain, upgrade domain, and public load balancer backend pool as the
built-in reverse proxy). This preserves the traffic shape during
coexistence in [§5.4](#54-cut-over-client-traffic-to-sfyarp).

Use `InstanceCount = -1` on both `YarpProxy.Service` and
`FabricDiscovery.Service`, and constrain placement to the ingress
node type with `YarpProxy_PlacementConstraints` (e.g.
`"NodeType==FrontEnd"`).

For the general placement guidance, `DefaultMoveCost` behavior,
resource governance policy syntax, and starting-point CPU/memory
values, see [README §Placement, sizing, and resource
governance](../README.md#placement-sizing-and-resource-governance).

---

## 8. TLS and certificate management

> **Does this section apply to you?** If your L7 gateway in front
> terminates TLS and forwards plain HTTP to SFYarp, you can skip
> most of this section (no server certificate needed on SFYarp),
> but you must lock down the L7-to-SFYarp path via NSG. See
> [§10](#10-network-lockdown-nsg-and-service-tags).

TLS setup is the most common source of migration surprises. For the
full green-field TLS setup, see [README §TLS and
certificates](../README.md#tls-and-certificates). That is the
canonical source. This section calls out **migration-specific
considerations** on top of it.

**HTTPS-first defaults during migration.**

- Terminate TLS on port `443` (or your chosen HTTPS port). Do not
  expose the HTTP port publicly — see
  [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster) step 2.
- Reject HTTP-only backend endpoints per route with
  `Yarp.Backend.AllowInsecureHttp=false` — see
  [§5.3](#53-opt-in-a-service-with-labels).
- Require modern TLS versions on backend calls with
  `Yarp.Backend.HttpClient.SslProtocols=Tls12,Tls13` when backends
  need it.

**Migration-specific considerations:**

- **Reuse or replace the existing cluster certificate.** If your
  cluster today uses a wildcard or SAN certificate that already
  covers SFYarp's hostname, reuse it. No new cert needed. If it
  does not, provision a new one (public CA, private CA, or Azure
  Key Vault-generated cert all work). See
  [README §TLS and certificates](../README.md#tls-and-certificates).
- **Deploy the SFYarp certificate before you label any service.**
  The cert must be present in `LocalMachine\My` on ingress nodes
  *before* the first service you opt in via
  [§5.3](#53-opt-in-a-service-with-labels) starts receiving traffic.
  Otherwise SFYarp activates without a cert and clients see TLS
  handshake failures during your first cutover attempt.
- **`SecretsCertificate` vs `EndpointCertificate`.** If any
  cert-consuming code paths in the SFYarp app package need Service
  Fabric-managed private-key ACLs, declare the cert as
  `<EndpointCertificate>` in `ApplicationManifest.xml`, **not** as
  `<SecretsCertificate>`. SF gates service activation on
  `<EndpointCertificate>` presence, which also acts as a safety net
  against the SFMC extension-ordering race on scale-out. On newer
  SFMC API versions (`2023-09-01-preview` or later) the primary fix
  is `setupOrder: ["BeforeSFRuntime"]` + `requireInitialSync: true`
  on the KV VM extension. This gating matters most on older API
  versions where `setupOrder` isn't available. See [README §Private
  key ACLs (`<EndpointCertificate>` declaration)](../README.md#private-key-acls-endpointcertificate-declaration)
  and [README §Extension ordering race on SFMC scale-out](../README.md#extension-ordering-race-on-sfmc-scale-out).
- **Coexistence period.** During
  [§5.4](#54-cut-over-client-traffic-to-sfyarp) the built-in reverse
  proxy still fronts port `19081` with the cluster cert. SFYarp uses
  its own cert on 443. The two certs can be identical or different —
  independent code paths, no interaction.

## 9. L7 gateway in front of SFYarp

SFYarp doesn't care what L7 gateway sits in front of it (Azure
Application Gateway, Azure Front Door, NGINX, etc. all work the same
way). If your migration includes swapping backend pools on an existing
L7 gateway from the built-in reverse proxy to SFYarp, the
migration-specific concern is the **backend pool swap sequence**:

1. Deploy SFYarp as a *new* backend pool alongside the existing
   built-in reverse proxy pool. Do not modify the existing pool.
2. Add SFYarp to the L7 routing rules with a small traffic share so
   you can measure real traffic before switching.
3. Shift traffic weight from the built-in reverse proxy pool to
   SFYarp per [§5.4](#54-cut-over-client-traffic-to-sfyarp).
4. Once cut over is complete and stable, remove the built-in reverse
   proxy pool.

For **all other L7 configuration** — dedicated `/proxy-health` probe
routes, preserving the client `Host` header on the backend hop (each
L7 has its own option name for this), and `X-Forwarded-*` header
ownership — see
[README §L7 gateway integration](../README.md#l7-gateway-integration).
That configuration is the same on SFRP and SFMC and is not
migration-specific.

---

## 10. Network lockdown (NSG and Service Tags)

The NSG shape for locking SFYarp down behind an L7 gateway is not
migration-specific. For the full walkthrough — source-address prefix
choice per L7 type (including Azure service tags such as
`AzureFrontDoor.Backend` when Front Door is the L7), SFMC's
`networkSecurityRules` mechanism vs. direct VMSS NSG edits, SFMC's
1000–3000 priority window, and internal-only patterns — see
[README §Network lockdown](../README.md#network-lockdown-nsg-and-service-tags).

**Migration-specific considerations:**

- **Coexistence port.** Until you complete
  [§5.5](#55-turn-off-the-built-in-reverse-proxy), SFYarp listens on
  your chosen coexistence port (typically `8443`), not `443`. Your
  NSG allow rules must target the coexistence port during
  cutover. Swap `destinationPortRange` to `443` in the same edit
  where you disable the built-in reverse proxy.
- **Existing NSG rules for the built-in reverse proxy.** If today
  you allow the L7 to reach the reverse proxy on `19081`, leave
  that rule in place until [§5.5](#55-turn-off-the-built-in-reverse-proxy)
  removes the built-in reverse proxy. Only then delete it. Otherwise
  a partial cutover leaves the built-in reverse proxy unreachable
  while some traffic is still routed at it.
- **Don't use the `ServiceFabric` service tag.** It targets the SF
  management port (~19080), not the reverse-proxy port. Called out
  again here because customers migrating off the built-in reverse
  proxy often reach for that tag by name.

## 11. Limitations and behavior changes

For SFYarp platform limits (Windows-only, HTTP/HTTPS only, SF 11.5+),
see [README §Compatibility and
limitations](../README.md#compatibility-and-limitations).

**Caller-visible behavior changes.** Existing clients will notice:

- **Services without labels return 404** instead of being
  auto-exposed. See [§5.3](#53-opt-in-a-service-with-labels).
- **`PartitionID=<guid>` only** ([§6](#6-client-url-translation)). No
  `PartitionKind` + `PartitionKey` translation at the proxy. Callers
  resolve the partition GUID once and cache per key.
- **`X-ServiceFabric` response header is not emitted**
  ([§6.5](#65-response-headers)). Clients that switch on
  `NoRetry` / `ResourceNotFound` must move to HTTP status codes.
- **No implicit retry on 404.** The built-in reverse proxy re-resolves service
  address on 404 (unless the backend sends
  `X-ServiceFabric: ResourceNotFound`). SFYarp passes 404 through.
  Services relying on this (typically http.sys / port-sharing hosts)
  need client-side retry.
- **Named-listener selection is per-service, not per-request.** A
  backend that needs different listeners per request must be
  redesigned, typically by publishing two service names.

**Sovereign clouds:**

- **Telemetry is on by default.** In Azure Government or China, set
  `YarpProxyEnableTelemetry=false` to avoid egressing telemetry
  across sovereign-cloud boundaries.

**Not affected by the migration.** These keep working unchanged:

- **SF Remoting** (`FabricTransport` / V2 remoting). Clients resolve
  endpoints through the SF Naming Service, not through any HTTP
  reverse proxy. Turning off the built-in reverse proxy has no
  impact on remoting traffic.
- **Direct Naming Service resolution.** Any code path that reaches
  backends without going through the built-in reverse proxy (for
  example, `ServicePartitionResolver` in the SF client SDK) keeps
  working unchanged.

---

## 12. Rollback plan

Rollback depends on how far you've progressed:

- **After Step 1 (SFRP + SFYarp coexisting).** Flip DNS or the LB back
  to the built-in reverse proxy port. You can delete
  `fabric:/YarpProxyApp` at leisure. Service-manifest labels added in
  [§5.3](#53-opt-in-a-service-with-labels) are inert without SFYarp
  routing — no need to revert them.
- **After Step 2 (built-in disabled on SFRP).** Re-enable the built-in
  reverse proxy through a cluster-manifest upgrade: set
  `ApplicationGateway/Http/IsEnabled=true` and re-add the reverse-proxy
  endpoint to each node type. A cluster-manifest upgrade rolls out over
  roughly one UD cycle.
- **After Step 3 (moved to SFMC).** You cannot roll back to the
  built-in reverse proxy on SFMC (it does not exist there). The
  rollback target is the previous SFRP cluster, which should remain in
  place (or be re-createable from ARM template) until you're confident
  in the SFMC cutover.

Keep the previous SFRP cluster's ARM template and last-known-good
cluster manifest in source control until you're confident in the SFMC
cutover.

**Application-level rollback (SFYarp upgrade only).** To roll back an
SFYarp application upgrade without touching the ingress topology,
flip the `applications` resource's `version` property in ARM back to
the previous `applicationTypes/versions` resource ID — see
[§5.2](#52-deploy-sfyarp-via-arm-recommended) step 4. This is why
keeping the previous `applicationTypes/versions` resource and its
SFPKG blob for at least one rollback window matters.

---

## 13. Troubleshooting

Start every SFYarp investigation by collecting logs. The symptom table
in [§13.2](#132-common-migration-symptoms-and-fixes) assumes you can see what SFYarp is
actually doing on the node.

### 13.1 Collecting SFYarp logs

For general SFYarp log sources — the Windows Event Log source
`YarpProxyLogs`, `ConsoleRedirection` for raw stdout/stderr during
development, and centralized forwarding via the SF diagnostic
extension — see [README §Collecting SFYarp
logs](../README.md#collecting-sfyarp-logs).

**Migration-specific tip.** During
[§5.4](#54-cut-over-client-traffic-to-sfyarp) coexistence, both the
built-in reverse proxy and SFYarp are handling traffic. Correlate
which one served a given request by the port (`19081` for built-in
vs your chosen coexistence HTTPS port for SFYarp). When a request
was proxied by SFYarp, correlate the client-visible request-id
header against `YarpProxyLogs` events on the node that answered.

If `YarpProxyLogs` has nothing yet on a given node (SFYarp failed
before its own logging came up), check the SF Hosting log
`Microsoft-ServiceFabric/Admin` for `ApplicationProcessExited`
events. During Step 1 that's the fastest signal that the
`fabric:/YarpProxyApp` deploy landed on some nodes but not others.

### 13.2 Common migration symptoms and fixes

Migration-specific issues (the "used to work through the built-in
reverse proxy, now broken" family):

| Symptom | Likely cause | Where to look |
|---|---|---|
| 404 from SFYarp for a service that used to work through the built-in reverse proxy | Service was not opted in with SFYarp labels | Target service's `ServiceManifest.xml` `<Extensions>` block. See [§5.3](#53-opt-in-a-service-with-labels) |
| SFYarp returns 404 for every path against a known-good backend | Client URL is missing the `/{AppName}/{ServiceName}/` prefix, or uses the built-in reverse proxy's `?PartitionKey=&PartitionKind=` query params which SFYarp doesn't honor | Rewrite the URL per [§6](#6-client-url-translation). For stateful backends use `?PartitionID=<guid>` per [§6.2](#62-stateful-int64-or-named) |
| `YarpProxy.Service` won't start (port bind fails) | Port already in use (built-in reverse proxy still on the same port during coexistence) | Move SFYarp to a coexistence port during [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster) step 2 |
| `YarpProxy.Service` crash-loops with `RemoteConfigWorker … abort timeout` in `YarpProxyLogs`, or a `.NET Runtime` crash referencing `RemoteConfigWorker` in the Application event log | Application URI doesn't match `fabric:/YarpProxyApp`, which SFYarp uses to discover `FabricDiscovery.Service` | Redeploy the application as `fabric:/YarpProxyApp` (see [§5.2](#52-deploy-sfyarp-via-arm-recommended)) |
| Client relied on the `X-ServiceFabric` response header (`NoRetry`, `ResourceNotFound`) | SFYarp doesn't emit that header | Update client to switch on HTTP status code / body. See [§6.5](#65-response-headers) and [§11](#11-limitations-and-behavior-changes) |

For any symptom that isn't specifically about migrating off the
built-in reverse proxy, see [README
§Troubleshooting](../README.md#troubleshooting) for the general
SFYarp troubleshooting reference.


## 14. FAQ

**Do I have to move to SFYarp if I stay on SFRP?**
No. The built-in reverse proxy continues to work on SFRP. SFYarp is
the only recommended reverse proxy on SFMC — so if there is any chance
you move to SFMC later, doing this migration now (while both are
available) is the least risky path.

**Can I run both proxies at the same time on the same cluster?**
Yes, on different ports. That is the intended state during Step 1 of
the [recommended migration path](#3-recommended-migration-path). Do
not run them on the same port.

**Do I need to re-issue certificates?**
Only if your current certificate doesn't have SANs for the hostnames SFYarp
will serve. Existing wildcards work fine — SNI just needs a SAN match.
See [README §Certificate naming (SAN/CN)](../README.md#certificate-naming-sancn) for cert-naming details.

**Can I convert built-in reverse-proxy config into SFYarp labels
mechanically?**
Only partially. Built-in `SecureOnlyMode` (reject HTTP from clients)
has no direct SFYarp label — the equivalent is to expose only the
HTTPS endpoint in SFYarp's service manifest and not publish an HTTP
endpoint. (SFYarp's `AllowInsecureHttp` is a *backend-discovery*
setting — it controls whether SFYarp will forward to non-HTTPS
backend endpoints, not whether it accepts non-HTTPS client requests.)
`RemoveServiceResponseHeaders` maps to per-route
`Transforms.ResponseHeaderRemove` labels. Most other cluster-level
settings (`NumberOfParallelOperations`, `BodyChunkSize`,
`HttpRequestConnectTimeout`) have no direct per-service YARP
equivalent. They are either handled by Kestrel/YARP defaults or exposed
via `Yarp.Backend.HttpRequest.*` labels per service.

---

## Appendix — SFYarp labels reference

Every label lives inside `<Extension Name="Yarp"><Labels>` in the
service's `ServiceManifest.xml`.

The labels you'll typically add during a migration:

| Label | Meaning |
|---|---|
| `Yarp.Enable` | Set to `true` to expose the service through SFYarp (**required**) |
| `Yarp.Routes.<name>.Path` | ASP.NET Core route template, e.g. `/{**catchall}` |
| `Yarp.Routes.<name>.Hosts` | Comma-separated hostnames to match (multi-tenant) |
| `Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode` | Replaces `?TargetReplicaSelector=`. Values: `PrimaryOnly` / `SecondaryOnly` / `All` |
| `Yarp.Backend.ServiceFabric.ListenerName` | Replaces `?ListenerName=`. Picks a named endpoint when the service exposes several |
| `Yarp.Backend.HttpRequest.ActivityTimeout` | Replaces `?Timeout=`. Per-service request timeout, e.g. `00:00:30` |
| `Yarp.Backend.HealthCheck.Active.Enabled` / `.Path` / `.Interval` / `.Timeout` | Proxy → backend active health checks (see [§5.3](#53-opt-in-a-service-with-labels)) |
| `Yarp.Backend.HttpClient.SslProtocols` | e.g. `Tls12,Tls13` when the backend requires it |
| `Yarp.Backend.AllowInsecureHttp` | Allow SFYarp to forward to a plain-HTTP backend. Current default is `true`. Set to `false` explicitly on every production route |
| `Yarp.Routes.<name>.Transforms.[N].*` | Header add / remove / rename, path transforms |

**For the complete label catalog** — route matching, backend behavior,
session affinity, load balancing, metadata, and every optional
parameter — see [README §Supported labels](../README.md#supported-labels).
That page is the source of truth and is updated with each release.
This appendix intentionally covers only the migration essentials to
avoid drift.

For the underlying YARP concepts (transforms, session affinity,
health-check policies), see the
[YARP documentation](https://github.com/dotnet/yarp).
