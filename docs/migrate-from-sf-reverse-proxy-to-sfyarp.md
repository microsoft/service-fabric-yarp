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

This migration guide walks you through replacing the built-in Service
Fabric Reverse Proxy with Service Fabric Yarp (SFYarp) — prerequisites,
how the two solutions compare, the recommended path, and the
operational concerns you may hit along the way.

This guide is migration-focused. For product docs (full label
catalog, deployment reference, sample apps, dev setup), see the
SFYarp [README](https://github.com/microsoft/service-fabric-yarp#readme).

It applies if you run one or more Service Fabric
applications behind the built-in reverse proxy — the Service Fabric
HTTP reverse-proxy component that listens on the "reverse proxy endpoint",
commonly port 19081 — and need a replacement. Two situations
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
   [§5.2](#52-opt-in-a-service-with-labels).
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
3. If moving to SFMC, migrate the cluster — the same SFYarp package
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
| Rollback | Cluster-manifest revert | `Start-ServiceFabricApplicationUpgradeRollback` |

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
    +---------------------------+
    |  SFRP + built-in proxy    |
    |  (baseline you have now)  |
    +---------------------------+
                 |
                 |  Step 1: Deploy SFYarp on non-conflicting ports.
                 |          Keep the built-in on; cut client traffic gradually.
                 v
    +---------------------------+
    |  SFRP + SFYarp            |
    |  built-in proxy still on  |
    +---------------------------+
                 |
                 |  Step 2: Disable the built-in in the cluster manifest.
                 |          Cluster is otherwise unchanged.
                 v
    +---------------------------+
    |  SFRP + SFYarp only       |
    +---------------------------+
                 |
                 |  Step 3: Migrate the cluster to SFMC.
                 |          The SFYarp package redeploys unchanged.
                 v
    +---------------------------+
    |  SFMC + SFYarp            |
    +---------------------------+
```

Why this ordering matters:

- **Step 1** validates SFYarp against your real routes, real TLS certificates,
  real backends, and real client SDKs — while the built-in reverse
  proxy is still live. If you find a routing gap, an authentication
  mismatch, or a header your client depended on, you cut traffic back
  with a DNS or load-balancer flip.
- **Step 2** removes the built-in reverse proxy from the cluster, but the
  cluster itself is unchanged. Any client regression here is easy to
  correlate ("the built-in proxy went away") without mixing it with the
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
- [ ] **Ingress topology:** whether Application Gateway or Front Door
      will sit in front of SFYarp — see
      [§9](#9-l7-gateway-in-front-of-sfyarp).
- [ ] **Capacity baseline:** plan a load test against SFYarp on your
      target VM SKU before cutting traffic — see
      [§7.1](#71-cpu-and-memory-sizing).
- [ ] **Ports:** which ports SFYarp will bind. Shipped defaults are
      `8080` (HTTP) and `443` (HTTPS). Keep SFYarp's ports **distinct
      from the built-in reverse proxy's port** (typically `19081`) so
      the two proxies can run side-by-side during migration. If your
      cluster already binds `443` for other ingress, pick a non-443
      HTTPS port (for example `8443`) until cutover — see
      [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster).
- [ ] **Rollback path:** confirm the L7 backend-pool edit that reverts
      traffic to the built-in proxy — see [§12](#12-rollback-plan).

> **Ownership shifts to your team.** The built-in reverse proxy was a
> platform component maintained by the SF runtime. SFYarp is an SF
> application your team owns — upgrade cadence, alerts, capacity, and
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
[§5.2](#52-opt-in-a-service-with-labels).

For every row, decide:

- **Should it be reachable via ingress?** If not, the service will be
  correctly kept private by SFYarp until you opt it in.
- **What partition scheme does it use?** Anything not `Singleton` needs
  its callers to switch from `PartitionKind`+`PartitionKey` to
  `PartitionID=<guid>` — see [§6](#6-client-url-translation).
- **Does it need a specific listener or replica role?** Check
  `ServiceManifest.xml` `<Endpoints>` for named listeners. Those
  become per-service labels — see [§5.2](#52-opt-in-a-service-with-labels).
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
   containing the ApplicationPackageRoot layout — the rest of this
   guide operates on that folder.
2. **Decide the ports.** Pick ports that don't collide with the
   built-in proxy's port (commonly 19081) or with anything else
   already bound to 443 on the ingress node type. The shipped
   defaults are `YarpProxy_HttpPort=8080` and
   `YarpProxy_HttpsPort=443`. For coexistence with the built-in
   proxy or other 443 ingress, override `YarpProxy_HttpsPort` to a
   non-conflicting value (e.g., 8443) while cutting over. Switch
   back to 443 after
   [§5.4](#54-turn-off-the-built-in-reverse-proxy) turns off the
   built-in proxy.

   > **In production, prefer HTTPS only.** Omit the HTTP port from the
   > LB/NSG in step 4.
3. **Deploy the SFYarp application via ARM.** Author the SFYarp
   application in the same ARM / Bicep template that provisions the
   cluster — see [§5.1a](#51a-deploy-sfyarp-via-arm-recommended) for
   the resource shape and rotation flow. A PowerShell publish path
   for dev/test iteration on an already-running cluster is available
   at the end of §5.1a.

4. **Open the SFYarp port on the load balancer and NSG.** Open the
   port on **both** the load balancer and the NSG — callers cannot
   reach SFYarp until both are in place.

   - **Classic SFRP** — add a `loadBalancingRules` entry (frontend port
     → VMSS backend pool → TCP probe) to your public load balancer,
     add the port to the node type's `frontendPorts`, and add an
     inbound `Allow` rule to the VMSS's NSG.
   - **SFMC** — add the rule to the `Microsoft.ServiceFabric/managedclusters`
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

5. **Label your first target service** (see [§5.2](#52-opt-in-a-service-with-labels)),
   then send a request to SFYarp from a test machine and confirm it
   lands on the intended backend.

### 5.1a Deploy SFYarp via ARM (recommended)

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
> [§8.4](#84-private-key-acls-for-sfyarp)), add a
> `<ServicePackageResourceGovernancePolicy>` for CPU/memory limits
> (see [§7.1](#71-cpu-and-memory-sizing)), tune placement via the
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
> `fabric:/YarpProxyApp` exactly — `YarpProxy.Service` uses this
> name to discover `FabricDiscovery.Service` in the cluster and it
> can't be overridden through configuration. Any other name causes
> `YarpProxy.Service` to crash-loop shortly after activation (see
> [§13.2](#132-common-symptoms-and-fixes)).

**3. Reference the SFPKG from ARM.** Add two resources under
`Microsoft.ServiceFabric/managedclusters`:

```json
[
  {
    "type":       "Microsoft.ServiceFabric/managedclusters/applicationTypes",
    "apiVersion": "2024-04-01",
    "name":       "[concat(parameters('clusterName'), '/YarpProxyAppType')]",
    "properties": {}
  },
  {
    "type":       "Microsoft.ServiceFabric/managedclusters/applicationTypes/versions",
    "apiVersion": "2024-04-01",
    "name":       "[concat(parameters('clusterName'), '/YarpProxyAppType/2.0.0')]",
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
  name in the shipped `ApplicationManifest.xml` — do **not** rename it.
- The version segment (`2.0.0`) matches the manifest version the
  release pipeline stamped in the shipped package.
- `YarpProxy_InstanceCount = "-1"` means "one instance per eligible
  node". `YarpProxy_PlacementConstraints` limits eligibility to the
  node types that should carry ingress — replace `FrontEnd` with your
  ingress node type. See [§7](#7-placement-and-sizing).
  `FabricDiscovery.Service` reuses `YarpProxy_PlacementConstraints`
  from the shipped manifest — no separate parameter is exposed. For
  HTTPS cert-selector parameters, see
  [§8](#8-tls-and-certificate-management).
- If your cluster template already provisions the node type with the
  KV VM extension ([§8.3](#83-deploying-certificates-to-nodes) Option 1),
  no additional certificate plumbing is needed here — the SFYarp application
  will find the certificate in `LocalMachine\My` at activation time.

**4. Upgrade to a new SFYarp version.** Fetch the new package version
per [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster) step 1, then
repeat steps 1–3 above — upload the new SFPKG to a **new**
version-in-path blob (don't overwrite; SFMC rejects in-place
`appPackageUrl` changes on an active version resource). Add a new
`applicationTypes/versions` resource pointing at it, then flip the
`applications` resource's `version` property to the new resource ID.
SFMC performs a monitored rolling upgrade honoring the cluster's
`upgradePolicy` — see
[Service Fabric application upgrade](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-upgrade)
and
[Application upgrade parameters](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-upgrade-parameters)
for the full parameter surface. Rollback is the same `version` flip
back to the previous resource ID — which is why version-in-path
storage matters. Keep the previous SFPKG and its
`applicationTypes/versions` resource around for at least one
rollback window.

<details>
<summary><strong>PowerShell publish (dev/test only)</strong></summary>

For dev/test iteration on an already-running cluster you can publish
the application from PowerShell instead of ARM. Do **not** use this
for production — production rollouts should go through the ARM path
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
        YarpProxy_HttpsPort            = "8443"   # coexistence port; switch to 443 after §5.4
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

### 5.2 Opt in a service with labels

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
  backend SNI validation. See [§8.6](#86-in-cluster-service-to-service-traffic)):
  ```xml
  <Label Key="Yarp.Routes.defaultRoute.Transforms.[0].RequestHeaderOriginalHost">true</Label>
  ```

> **Containers.** Containerized SF services need no SFYarp-specific
> configuration — opt them in with the same `ServiceManifest.xml`
> labels as any other service. SFYarp routes to whichever endpoint
> SF publishes in the Naming Service.

### 5.3 Cut over client traffic to SFYarp

> **`Yarp.Enable=true` is additive.** Opting a service in makes it
> reachable via SFYarp but does not remove it from the built-in
> reverse proxy — both paths serve the service until you turn off the
> built-in reverse proxy cluster-wide in [§5.4](#54-turn-off-the-built-in-reverse-proxy).

Once SFYarp is running and at least one service is labeled, roll it in
behind your existing ingress in this order to avoid dropped traffic:

1. **Verify SFYarp is healthy** from a node-local `curl` against
   `/proxy-health` (or whichever route you've reserved for the L7
   probe — see [§9.1](#91-health-probes)) before touching any
   ingress config.
2. **Add SFYarp to the L7 backend pool** (Application Gateway, Front
   Door, or whatever sits in front) as a *second* backend alongside the
   built-in reverse proxy. Both proxies receive traffic — SFYarp
   handles whatever it has labels for, and the built-in proxy handles
   the rest.
3. **Opt in services one at a time** (see [§5.2](#52-opt-in-a-service-with-labels)).
   Verify each service works through SFYarp before opting in the
   next. This is the point where you catch label misconfigurations,
   HTTPS-vs-HTTP mismatches, and route conflicts.
4. **Drain the built-in reverse proxy** by removing it from the L7
   backend pool once all opted-in services are stable. Keep it
   running on the cluster for a rollback window.
5. **Turn off the built-in reverse proxy** ([§5.4](#54-turn-off-the-built-in-reverse-proxy))
   only after the rollback window passes with no incidents.

Do **not** put both proxies behind the same LB pool without an L7 in
front doing path-based selection. Clients will hit either proxy at
random, and response semantics differ — behavior will be inconsistent.

> **No L7 in front?** If clients hit the LB directly, cut over by
> pointing the LB frontend port at SFYarp's backend port instead of
> the built-in proxy's — a single LB rule swap. Rollback is the
> reverse swap. Both proxies keep running on their own ports during
> the transition.

### 5.4 Turn off the built-in reverse proxy

> **SFMC skip.** This section only applies on SFRP. SFMC never shipped
> the built-in reverse proxy — there is nothing to turn off. If you
> are on SFMC, continue to [§5.5](#55-move-the-cluster-to-sfmc).

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
   coexistence), you can now switch it to 443 via
   `Start-ServiceFabricApplicationUpgrade` with new port parameters.

### 5.5 Move the cluster to SFMC

Perform your normal SFRP → SFMC migration. Because the SFYarp package
is already validated on SFRP:

- Author the SFYarp application in the same ARM / Bicep template that
  provisions the managed cluster — see
  [§5.1a](#51a-deploy-sfyarp-via-arm-recommended). Since §5.1a is
  already how you deployed SFYarp on SFRP, the SFMC deployment shares
  the same template shape.
- Labels travel with the application package — no re-labeling needed
  on SFMC.
- Cut traffic the same way as [§5.3](#53-cut-over-client-traffic-to-sfyarp). See [§9](#9-l7-gateway-in-front-of-sfyarp) if Application Gateway or Front Door sits in front.

---

## 6. Client URL translation

> **Path-prefix note.** SFYarp preserves the built-in proxy's
> `/{AppName}/{ServiceName}/…` path prefix (see the base-URL columns
> in the tables below). Only the query-string contract changes. Calls
> to bare backend paths — for example `/hello` instead of
> `/PingerApp/PingerService/hello` — return 404. See
> [§13.2](#132-common-symptoms-and-fixes).

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
| Target replica role | Per-request `&TargetReplicaSelector=`; default is `PrimaryReplica` | Per-service label `Yarp.Backend.ServiceFabric.StatefulReplicaSelectionMode`; default is `PrimaryOnly` |

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
| Per-request override | `?Timeout=30` (seconds); default is 120 seconds | Not per request; set the label `Yarp.Backend.HttpRequest.ActivityTimeout=00:00:30` on the target service |

Callers that never set `Timeout=` explicitly relied on the built-in's
120-second default. Set an explicit `ActivityTimeout` label on migrated
services if that timeout mattered to your workload — the YARP
default is shorter (100 seconds).

### 6.5 Response headers

The built-in proxy emits `X-ServiceFabric: NoRetry` on failures the
gateway won't retry, and `X-ServiceFabric: ResourceNotFound` on
name-resolution misses. SFYarp emits **neither**. Clients that switch
on these must be updated to look at HTTP status code and body only.

---

## 7. Placement and sizing

**Recommended defaults:**

- **Do not** run SFYarp on the system / primary node type. That node
  type is reserved for fabric services and isn't typically in the public
  LB probe path.
- **Do** run SFYarp on the same node type(s) that already carry your
  ingress-facing services — same fault domain, same upgrade domain,
  same LB backend pool. This matches how the built-in reverse proxy is
  positioned on your SFRP cluster today.
- **Use `InstanceCount = -1`** for both `YarpProxy.Service` and
  `FabricDiscovery.Service`. This asks Service Fabric to place one
  instance per eligible node. As the node type scales in or out, SFYarp
  scales with it automatically. Do not hard-code a fixed number.
- **Constrain placement to the ingress node type** with
  `YarpProxy_PlacementConstraints`. Example:
  ```text
  YarpProxy_PlacementConstraints = "NodeType==FrontEnd"
  ```
  The shipped manifest wires the same constraint into
  `FabricDiscovery.Service` — no separate parameter is exposed.
  On SFMC the constraint uses whatever node-type name your managed
  cluster resource exposes.

The SFYarp application manifest sets `DefaultMoveCost="High"` on
`YarpProxyType` so the Service Fabric Cluster Resource Manager
won't move the proxy during normal balancing.

### 7.1 CPU and memory sizing

The SFYarp package does **not** include a
[`<ServicePackageResourceGovernance>`](https://learn.microsoft.com/azure/service-fabric/service-fabric-resource-governance)
limit by default. The following conservative values are a safe starting
point for initial deployment — they give SFYarp headroom for spikes
without starving co-located services on the node. Baseline against
representative traffic and tighten once you have steady-state
working-set and CPU numbers from your target VM SKU.

| Package | CpuCores | MemoryInMB |
|---|---|---|
| `YarpProxy.Service` | 1 | 1024 |
| `FabricDiscovery.Service` | 0.5 | 512 |

Apply the limit in `ApplicationManifest.xml`:

```xml
<ServiceManifestImport>
  <ServiceManifestRef ServiceManifestName="YarpProxy.ServicePkg" ServiceManifestVersion="..."/>
  <Policies>
    <ServicePackageResourceGovernancePolicy CpuCores="1" MemoryInMB="1024"/>
  </Policies>
</ServiceManifestImport>
```

---

## 8. TLS and certificate management

TLS setup is the most common source of migration surprises. Read this
section end-to-end before provisioning.

**HTTPS-first defaults.** In production:

- Terminate TLS on port 443 (or your chosen HTTPS port). Do not
  expose the HTTP port publicly — see
  [§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster) step 2.
- Reject HTTP-only backend endpoints per route with
  `Yarp.Backend.AllowInsecureHttp=false` — see
  [§5.2](#52-opt-in-a-service-with-labels).
- Require modern TLS versions on backend calls with
  `Yarp.Backend.HttpClient.SslProtocols=Tls12,Tls13` when backends
  need it — see [§8.8](#88-sfyarp-to-backend-tls).

> **Does this section apply to you?** This section assumes SFYarp
> terminates TLS on port 443. If your L7 in front terminates TLS and
> forwards plain HTTP to SFYarp, you can skip §8 (no server certificate
> needed on SFYarp) — but you must lock down the L7-to-SFYarp path
> via NSG (see [§10](#10-network-lockdown-nsg-and-service-tags)).

### 8.1 SNI certificate selection

Kestrel binds with **SNI** — during the TLS handshake, the client's
ClientHello `server_name` (SNI) extension selects the matching certificate
by SAN. This happens **before** any HTTP request is parsed, so the
HTTP `Host` header is not what drives certificate selection. TLS **1.2 and
1.3** are on by default.

`YarpProxy.Service` enumerates certificates in `LocalMachine\My` at
startup and keeps the ones that:

- are currently valid
- have a private key
- chain-validate
- carry the Server Authentication EKU

### 8.2 Certificate naming (SAN/CN)

The certificate must have a **SAN** (or CN, for legacy clients) that matches
the hostname your callers use. Concretely:

- If callers use a DNS name like `https://api.contoso.com`, the certificate
  must have `api.contoso.com` in its SAN.
- If callers use the cluster FQDN `https://<cluster-fqdn>`, the certificate
  must have the cluster FQDN in its SAN.
- SFMC ships a default cluster certificate whose subject does **not**
  match your DNS name. **Don't rely on it for SFYarp.** Provision your
  own certificate.
- Certificates from a public CA, your organization's private CA, or
  Key Vault-generated certificates all work. Public callers need a
  publicly-trusted issuer. In-cluster callers can accept a private CA.

> **If your certificate is not from a publicly-trusted CA.** SFYarp filters
> out any certificate from `LocalMachine\My` that fails chain validation
> ([§8.1](#81-sni-certificate-selection)), so any
> issuer the machine doesn't already trust must be present in
> `LocalMachine\Root`:
>
> - **Publicly-trusted CA** (DigiCert, Let's Encrypt, GlobalSign,
>   etc.): nothing extra — the roots ship with Windows.
> - **Private CA / internal CA:** deploy the CA's root certificate (and any
>   intermediates) to `LocalMachine\Root` on every node.
> - **Self-signed** (dev / test only): the leaf is its own root, so
>   the same certificate lands in both `LocalMachine\My` and
>   `LocalMachine\Root`.

### 8.3 Deploying certificates to nodes

Store the certificate (with private key) in Azure Key Vault as a
**Certificate** (not a Secret) — a PFX-backed KV certificate is
required either way. From there you have two supported paths to land
it in `LocalMachine\My` on the ingress node type. Pick one based on
your rotation model.

**Option 1 (recommended default): Azure Key Vault VM extension (polling-based auto-rotation).**

The Key Vault VM extension polls Key Vault on a configurable interval
and installs new versions into `LocalMachine\My` on the running node.
Rotate certificates by publishing a new version to Key Vault — no ARM update,
no node-type redeploy. This is the recommended default for teams
whose recovery playbook is "issue a new certificate in KV" rather than
"redeploy infrastructure."

The cost is that the extension participates in extension ordering. On
SFRP you own the VMSS and can order it with `provisionAfterExtensions`
directly. On SFMC, use `setupOrder: ["BeforeSFRuntime"]` on the
extension — requires API version `2023-09-01-preview` or later. See
[§8.5](#85-extension-ordering-race-on-sfmc-scale-out) if you're on an
older SFMC API version.

> **Managed-identity prerequisites (Option 1 only).** `KVVMExtension`
> uses a managed identity attached to the SFYarp nodetype to fetch
> KV secrets at runtime. Before deploying the extension:
>
> - Attach a user-assigned or system-assigned identity to the SFYarp
>   nodetype and grant it read on the KV: `Key Vault Secrets User`
>   (RBAC), or `Get + List` on a legacy access policy.
> - For user-assigned identities on SFMC, grant the **Service Fabric
>   managed cluster resource provider** the `Managed Identity
>   Operator` role on the identity so it can attach the identity to
>   the underlying VMSS. Missing this grant fails deploy with
>   `LinkedAuthorizationFailed` (see
>   [§13.2](#132-common-symptoms-and-fixes)).
>
> Full RBAC and template shape:
> [Add a managed identity to a Service Fabric managed cluster node type](https://learn.microsoft.com/en-us/azure/service-fabric/how-to-managed-identity-managed-cluster-virtual-machine-scale-sets).

On SFMC, add the KV VM extension to the node type resource under
`vmExtensions[]`:

```json
{
  "type": "Microsoft.ServiceFabric/managedclusters/nodetypes",
  "apiVersion": "2023-09-01-preview",
  "properties": {
    "vmExtensions": [
      {
        "name": "KVVMExtension",
        "properties": {
          "publisher":              "Microsoft.Azure.KeyVault",
          "type":                   "KeyVaultForWindows",
          "typeHandlerVersion":     "3.0",
          "autoUpgradeMinorVersion": true,
          "setupOrder":             [ "BeforeSFRuntime" ],
          "settings": {
            "secretsManagementSettings": {
              "pollingIntervalInS":         "3600",
              "observedCertificates": [
                "https://<your-kv>.vault.azure.net/secrets/YarpProxySslCert"
              ],
              "certificateStoreName":       "My",
              "certificateStoreLocation":   "LocalMachine",
              "linkOnRenewal":              true,
              "requireInitialSync":         true
            }
          }
        }
      }
    ]
  }
}
```

The `setupOrder: ["BeforeSFRuntime"]` field forces the extension to
run to completion before the SF node extension starts, so
cert-consuming services always see the certificates at activation time. See
[Provision extensions before Service Fabric runtime](https://learn.microsoft.com/en-us/azure/service-fabric/how-to-managed-cluster-vmss-extension#how-to-provision-before-service-fabric-runtime).

- **Keep both `setupOrder: ["BeforeSFRuntime"]` and
  `requireInitialSync: true`** on `KVVMExtension` (both shown in the
  sample above). They gate different things: `setupOrder` schedules
  the extension before the SF runtime; `requireInitialSync` makes the
  extension report success only after the first certificate fetch completes.
  Dropping either leaves a window where SFYarp activation can race
  the initial certificate import — see
  [§8.5](#85-extension-ordering-race-on-sfmc-scale-out).

On classic SFRP, put the extension on the VMSS resource and use
`provisionAfterExtensions` on the SF node extension to chain it
behind KV — see [§8.5](#85-extension-ordering-race-on-sfmc-scale-out).

**Option 2: `vmSecrets` on the node type.**

`vmSecrets` uses Azure VMSS's native `osProfile.secrets` plumbing —
certificates are injected during VM provisioning, **before any VM extension
runs**. This sidesteps the extension-ordering race in
[§8.5](#85-extension-ordering-race-on-sfmc-scale-out) entirely and
does not depend on any SFMC API-version feature.

Rotation is done by updating the ARM template with the new versioned
certificate URL and re-deploying the node type.

**Choose Option 2 if any of these apply:**

- You can't set `setupOrder: ["BeforeSFRuntime"]` (SFMC API older than
  `2023-09-01-preview`. See [§8.5](#85-extension-ordering-race-on-sfmc-scale-out)).
- You want certificate rotation to go through your ARM/CI pipeline — not a
  background poll from Key Vault.
- Your nodes can't reach Key Vault at runtime.
- You want certificates present before any VM extension runs, structurally.

On SFMC, add `vmSecrets` to the node type resource:

```json
{
  "type": "Microsoft.ServiceFabric/managedclusters/nodetypes",
  "properties": {
    "vmSecrets": [
      {
        "sourceVault": { "id": "<KV resource ID>" },
        "vaultCertificates": [
          {
            "certificateUrl": "https://<kv>.vault.azure.net/secrets/YarpProxySslCert/<version>",
            "certificateStore": "My"
          }
        ]
      }
    ]
  }
}
```

On classic SFRP, put the same `secrets` block on the VMSS resource
under `osProfile.windowsConfiguration.secrets`.

**KV VM extension configuration — SFYarp-specific bits.**

The KV VM extension has a generic configuration surface
(`typeHandlerVersion`, `pollingIntervalInS`, `linkOnRenewal`,
`requireInitialSync`, etc.) that is independent of SF. Pick those per
the [Key Vault VM extension docs](https://learn.microsoft.com/en-us/azure/virtual-machines/extensions/key-vault-windows).
For SFYarp there are two extras beyond the SFMC snippet above:

- `certificateStoreLocation: LocalMachine` and
  `certificateStoreName: My` are required — SFYarp enumerates
  `LocalMachine\My` for SNI selection. Any other store is invisible
  to Kestrel. List every certificate SFYarp needs (SSL cert plus any
  cluster or application-secret certs) in `observedCertificates`.
- On classic SFRP, add `provisionAfterExtensions` on the SF node
  extension so it runs after the KV extension. On SFMC, use
  `setupOrder: ["BeforeSFRuntime"]` on the KV extension itself as
  shown above. See [§8.5](#85-extension-ordering-race-on-sfmc-scale-out)
  for the ordering race this addresses.

**Verify certificates on each node (both options).**

After deployment, run this on any ingress node to confirm the certificate(s)
are present with the expected SAN:

```powershell
Get-ChildItem cert:\LocalMachine\My |
  Where-Object { $_.HasPrivateKey } |
  Select-Object Thumbprint, Subject,
    @{n='SAN';e={($_.Extensions |
      Where-Object { $_.Oid.FriendlyName -eq 'Subject Alternative Name' }).Format(1)}},
    NotAfter
```

### 8.4 Private key ACLs for SFYarp

Declare the certificate in the SFYarp `ApplicationManifest.xml` as an
`EndpointCertificate`. When Service Fabric activates the application,
it ACLs the certificate's private key for the account that hosts the endpoint
(Network Service by default).

Add an `<EndpointCertificate>` entry to the SFYarp
`ApplicationManifest.xml` pointing at the certificate:

```xml
<ServiceManifestImport>
  <ServiceManifestRef ServiceManifestName="YarpProxyPkg" ServiceManifestVersion="..."/>
</ServiceManifestImport>

<Certificates>
  <EndpointCertificate Name="YarpProxyCert" X509FindType="FindBySubjectName" X509FindValue="[ServiceSslCertificateCommonName]" X509StoreName="My"/>
</Certificates>
```

> **No `<Policies>` block required.** Service Fabric grants Network
> Service read access on the private key from the
> `<EndpointCertificate>` declaration alone. To avoid first-boot
> activation retries, ensure `KVVMExtension` has both
> `setupOrder: ["BeforeSFRuntime"]` and `requireInitialSync: true`
> (see [§8.3](#83-deploying-certificates-to-nodes) Option 1) so the
> certificate is imported before SFYarp activates.

> **Prefer `FindBySubjectName` over `FindByThumbprint`.** Thumbprint
> changes on every renewal, forcing an application upgrade each time.
> `FindBySubjectName` is stable across renewals — pair it with a
> parameterized common name so the same package works across
> environments. The shipped SFYarp manifest doesn't expose this
> parameter by default. Add it to `<Parameters>`:

```xml
<Parameters>
  <Parameter Name="ServiceSslCertificateCommonName" DefaultValue="" />
</Parameters>
```

> **`SecretsCertificate` vs `EndpointCertificate` — an important
> asymmetry at activation time.** When Service Fabric activates a code
> package, these two manifest declarations behave differently if the
> referenced certificate is not yet present in `LocalMachine\My`:
>
> - **`SecretsCertificate` (with `SecurityAccessPolicy`)** — SF does
>   **not** gate activation on the certificate existing. The code package
>   starts successfully and only fails later when it tries to use the
>   certificate (for TLS, for decrypting protected settings, etc.). This is
>   the silent-broken-state pattern that makes scale-out fragile.
> - **`EndpointCertificate`** — SF gates the HTTPS endpoint binding
>   on the certificate being present. If the certificate isn't there, endpoint
>   binding fails and Service Fabric restarts the code package under
>   the standard health-monitored retry policy, so the service
>   naturally waits for the certificate to arrive.
>
> This asymmetry is why declaring a protected-settings certificate as an
> `EndpointCertificate` — even when it isn't strictly used for an
> endpoint — turns "silently broken" into "fail-fast, retry,
> self-heal." See [§8.5](#85-extension-ordering-race-on-sfmc-scale-out)
> for when this workaround applies.

### 8.5 Extension ordering race on SFMC scale-out

> **Skip this section if any of the following applies:**
> - You chose Option 2 (`vmSecrets`) in [§8.3](#83-deploying-certificates-to-nodes) —
>   `vmSecrets`-installed certificates are present in `LocalMachine\My` before
>   any extension runs, so the race below cannot occur.
> - You chose Option 1 (KV VM extension) on SFMC API
>   `2023-09-01-preview` or later and set `setupOrder: ["BeforeSFRuntime"]`
>   on the extension — this is the first-class ordering knob and
>   defeats the race for cert-consuming SF services.
>
> Read on if you chose Option 1 on an older SFMC API version, if you
> have other cert-consuming code paths that depend on
> extension-installed certificates, or if you want to understand the failure
> mode before making a platform choice.

This is the most important thing to plan for on SFMC when you use the
Key Vault VM extension. There is no equivalent problem on classic SFRP
because you own the VMSS and can order extensions there.

**Classic SFRP.** You own the VMSS, so you can chain the SF node
extension behind the KV VM extension with `provisionAfterExtensions`.
The SF node extension will not run until the KV extension has synced
certificates. Example:

```json
{
  "name": "ServiceFabricNodeExtension",
  "properties": {
    "type": "ServiceFabricNode",
    "autoUpgradeMinorVersion": true,
    "protectedSettings": {
      "StorageAccountKey1": "...",
      "StorageAccountKey2": "..."
    },
    "provisionAfterExtensions": [
      "DSCExtension",
      "KVVMExtension"
    ]
  }
}
```

The key line is `"KVVMExtension"` inside `provisionAfterExtensions`
on the SF node extension — without it, the SF node extension and the
KV extension race, exactly like they do on SFMC.

**SFMC.** SFMC owns the VMSS, so `provisionAfterExtensions` on the
underlying VMSS is not settable. But on SFMC API `2023-09-01-preview`
or later, `vmExtensions[]` items on
`Microsoft.ServiceFabric/managedclusters/nodetypes` accept
`setupOrder: ["BeforeSFRuntime"]` — set this on the KV VM extension
and it will install to completion before the SF node extension
starts, which is exactly the ordering guarantee the SFRP
`provisionAfterExtensions` pattern gives you. See
[Provision extensions before Service Fabric runtime](https://learn.microsoft.com/en-us/azure/service-fabric/how-to-managed-cluster-vmss-extension#how-to-provision-before-service-fabric-runtime).

On older SFMC API versions the ordering knob is not available, and on
scale-out events this manifests as:

- SF starts placing applications on a new node.
- SFYarp (and any other cert-consuming service) starts before the KV
  extension finishes installing certificates.
- SFYarp binds Kestrel, then fails HTTPS handshakes because the SNI
  callback finds no matching certificate (or, worse, finds a certificate whose
  private key it can't read).
- Any application that decrypts protected settings from certificates may fail
  during startup.

**Primary mitigation.** On SFMC, set `setupOrder: ["BeforeSFRuntime"]`
on the KV VM extension. Requires SFMC API version
`2023-09-01-preview` or later. This is the first-class ordering knob
— the KV VM extension runs to completion before the SF node
extension starts, so cert-consuming services always see their certificates
at activation time. See
[Provision extensions before Service Fabric runtime](https://learn.microsoft.com/en-us/azure/service-fabric/how-to-managed-cluster-vmss-extension#how-to-provision-before-service-fabric-runtime).
If your SFMC template is already on `2023-09-01-preview`+, this alone
eliminates the race — the items below become optional hardening.

**Additional hardening** (apply when the primary mitigation isn't
available, or for cert-consuming code paths outside SF's activation
gate):

- **Retry in the application.** Cert-consuming code paths tolerate
  "certificate not present yet" for the first few minutes after a node comes
  up. Cheap and centralizable via a startup script that waits for
  expected thumbprints.
- **Use KV VM extension 4.0 (or 3.0 with `requireInitialSync: true`).**
  The extension reports success only after every configured certificate is
  installed — closes the "extension green but certificates not yet there"
  window. Does **not** fix ordering with respect to the SF node
  extension.
- **Declare protected-settings certificates as `EndpointCertificate`.** Gates
  activation on the certificate's presence so SF's standard retry policy
  papers over the extension race — see the `SecretsCertificate` vs
  `EndpointCertificate` callout in
  [§8.4](#84-private-key-acls-for-sfyarp). Trade-off: the certificate must
  chain-validate at activation time, so self-signed / private-CA
  certificates need supporting root-store deployment.

Symptom on scale-out: freshly added nodes serve TLS errors for the
first few minutes after joining the cluster, then recover on their own.

### 8.6 In-cluster service-to-service traffic

If your services call each other through the built-in reverse proxy
today (that is: they hit `http://localhost:19081/...` on the same
node), the same pattern works with SFYarp. The recommended approach is a dedicated
`CN=localhost` certificate. The hosts-file pattern below is a fallback for
cases where you must preserve the cluster's public hostname on the
backend request.

> **Both patterns below assume SFYarp is co-located with the caller.**
> A `localhost` call only reaches SFYarp if the SFYarp service is
> running on the same node as the caller — for example, an
> `InstanceCount=-1` stateless deployment on every node the callers
> run on. If SFYarp is only on a dedicated ingress node type,
> in-cluster callers on other node types cannot use `localhost` and
> must call SFYarp by its cluster-internal hostname / port instead.

**Recommended: `CN=localhost` certificate.**

1. Provision a second certificate with `CN=localhost` (from your private CA,
   or self-signed for dev / test) and land it in `LocalMachine\My`
   alongside your public-hostname certificate. Both paths in
   [§8.3](#83-deploying-certificates-to-nodes) support multiple certificates.
2. In-cluster clients call
   `https://localhost:<yarp-https-port>/<App>/<Service>/...`. Kestrel
   SNI picks the `localhost` certificate.
3. The calling service must trust the issuer — if the certificate is
   privately issued, deploy the CA's root certificate to `LocalMachine\Root`
   on the caller's node.

**Fallback: hosts-file pattern.**

Use this only if in-cluster callers need to preserve the cluster's
public hostname (`something.contoso.com`) on the backend request —
for example, because the backend does SSL server-name validation on
the `Host` header.

1. Give SFYarp a certificate with the cluster's public hostname in its SAN.
2. On each node, add a hosts-file entry pointing that hostname to
   `127.0.0.1` — for example via the VMSS DSC extension or a startup
   custom script:
   ```
   127.0.0.1  something.contoso.com
   ```
3. Have in-cluster clients call
   `https://something.contoso.com:<yarp-https-port>/<App>/<Service>/...`.
4. On the target route, set the label
   `Yarp.Routes.<r>.Transforms.[0].RequestHeaderOriginalHost=true`
   so the original `Host` header is preserved when SFYarp forwards to
   the backend.

### 8.7 Certificate rotation

Push a new version to Key Vault. The KV VM extension imports it into
`LocalMachine\My` at its next poll (`pollingIntervalInS`) and SFYarp
picks up the new certificate on subsequent TLS handshakes. With the
`<EndpointCertificate>` declaration from
[§8.4](#84-private-key-acls-for-sfyarp), Service Fabric grants
`NT AUTHORITY\NETWORK SERVICE` (the identity SFYarp runs under by
default) read access on the newly imported private key automatically.
No ARM update, cluster upgrade, or node restart is required.

Existing TLS connections continue on the previous certificate until they
close naturally. New handshakes use the new certificate. Expect propagation
within a few polling intervals.

Verify propagation on any node with `certutil -store My` — both the
previous and the new thumbprint should be listed after the extension
polls. Do not remove the previous certificate from `LocalMachine\My` until
the new certificate has been confirmed present on every node. SNI needs a
matching certificate in the store to serve any TLS handshake.

If you deployed via [§8.3 Option 2 (`vmSecrets`)](#83-deploying-certificates-to-nodes),
rotation is a standard VMSS `osProfile.secrets` redeploy of the node
type — no SFYarp-specific step is required.

### 8.8 SFYarp-to-backend TLS

When a backend service publishes an HTTPS endpoint, SFYarp acts as a
TLS client to that endpoint. Behavior follows standard .NET
`HttpClient` defaults — the backend certificate must chain-validate and its
subject must match the target name. Three common gotchas during
migration:

- **Private-CA backends.** If backends use a private / internal CA, the
  CA's root certificate must be present in `LocalMachine\Root` on every SFYarp
  node, same as the inbound-cert rule in
  [§8.2](#82-certificate-naming-sancn). Without it, every
  backend handshake fails chain validation.
- **Backend certificate SAN.** SFYarp resolves the backend's IP:port from
  Naming Service and then handshakes. The backend certificate must have a
  SAN (or CN) that matches the hostname SFYarp uses on the handshake
  — commonly the cluster's node DNS suffix. A wildcard SAN like
  `*.<cluster>.<region>.cloudapp.azure.com` covers this cleanly.
- **Pinned TLS version.** If the backend requires TLS 1.2 or 1.3
  specifically, set it explicitly on the route:
  ```xml
  <Label Key="Yarp.Backend.HttpClient.SslProtocols">Tls12,Tls13</Label>
  ```

**Escape hatch (dev / test only).**

```xml
<Label Key="Yarp.Backend.HttpClient.DangerousAcceptAnyServerCertificate">true</Label>
```

Disables chain and hostname validation on backend calls. Do not ship
this to production — it silently accepts any certificate the backend presents.

**Backend mTLS.** SFYarp does not currently support attaching a client
certificate on outbound calls. Terminate mTLS at SFYarp and forward the
caller's identity in a signed header via a transform.

---

## 9. L7 gateway in front of SFYarp

Common topology: `Client → Application Gateway (or Front Door) → SFYarp → SF service`.
When SFYarp sits behind an L7 like Application Gateway or Front Door,
two things in the L7 configuration are worth getting right up front —
everything else follows the L7's normal setup.

### 9.1 Health probes

Point the L7 probe at a dedicated `/proxy-health` route on the SFYarp
backend pool. Any lightweight SF service that returns `200 OK` on a
known path will do — an existing infra "ping" service, or a minimal
ASP.NET Core stateless service added to the cluster. On that service,
add the standard SFYarp labels plus the `/proxy-health` route:

```xml
<Label Key="Yarp.Enable">true</Label>
<Label Key="Yarp.Routes.proxy-health.Path">/proxy-health</Label>
```

The endpoint should return `200 OK` with no DB call and no downstream
hop, so that when a business backend goes unhealthy the probe still
returns 200 and the L7 keeps SFYarp in rotation. **Do not** point the
L7 probe at a business path like `/PaymentApi/status` — if Payment
goes down, every SFYarp instance gets marked dead and `/OrderApi`
traffic goes down with it.

### 9.2 Preserving the client's Host header

Both Application Gateway and Front Door rewrite the `Host` header on
the backend hop by default, which breaks any SFYarp route matched on
`Host`. Configure the L7 to preserve the client's Host:

- **Application Gateway**: on the backend HTTP settings, leave *Pick
  host name from backend address* off
  (`pickHostNameFromBackendAddress=false`) and don't set *Host name
  override* (`hostName`). Both live on the same backend HTTP settings
  blade / ARM object.
- **Front Door**: on the origin group, leave *Origin host header*
  blank.

If none of your SFYarp routes match on `Host`, this doesn't matter —
otherwise those routes never fire.

**`X-Forwarded-*` headers.** Both the L7 in front and SFYarp emit
`X-Forwarded-For` / `X-Forwarded-Proto` / etc. Decide who is
authoritative — typically the L7 sets them at the edge and SFYarp
forwards them unchanged — and configure the other side not to
overwrite them.

---

## 10. Network lockdown (NSG and Service Tags)

When SFYarp is publicly reachable, restrict inbound traffic on the
ingress node type's public IP(s) to just the L7 in front of it.
Standard Azure networking applies, with two things worth calling out
for SFYarp on SFMC:

- **Source-address prefix depends on the L7.** For **Application
  Gateway**, allow the Application Gateway subnet's CIDR (or
  `VirtualNetwork` when the Application Gateway and the cluster share
  a vnet) — there is no service tag for Application-Gateway-to-backend
  traffic. `GatewayManager` is the Azure control-plane tag for
  Application Gateway's *own* subnet, not for backends behind it. For
  **Front Door**, allow the `AzureFrontDoor.Backend` service tag on
  the HTTPS port. In both cases, deny the rest of `Internet`. Do
  **not** use the `ServiceFabric` service tag — it targets the SF
  management port (~19080), not the reverse-proxy port.
- **On SFMC, edit the NSG through the managed cluster resource**
  (`networkSecurityRules` on the managed cluster ARM resource).
  Rules added directly to the underlying VMSS will be reverted by
  SFMC reconciliation. See
  [SFMC network security rules](https://learn.microsoft.com/azure/service-fabric/how-to-managed-cluster-networking#network-security-rules).
- SFMC's default `SF-NSG` denies Internet at priority 2900. Your
  allow rule for the SFYarp port must sit below 2900 and within
  SFMC's accepted priority range of **1000–3000**. See
  [SFMC networking](https://learn.microsoft.com/azure/service-fabric/how-to-managed-cluster-networking).

Concrete SFMC example — allow Application Gateway on port 443 and
deny everything else from the internet on that port (merge under the
`Microsoft.ServiceFabric/managedclusters` resource):

```json
{
  "type":       "Microsoft.ServiceFabric/managedclusters",
  "apiVersion": "2024-04-01",
  "name":       "[parameters('clusterName')]",
  "properties": {
    "networkSecurityRules": [
      {
        "name": "AllowAppGatewayToSfYarp",
        "protocol": "tcp",
        "sourcePortRange": "*",
        "sourceAddressPrefix": "10.0.1.0/24",
        "destinationAddressPrefix": "*",
        "destinationPortRange": "443",
        "access": "Allow",
        "priority": 2001,
        "direction": "Inbound"
      },
      {
        "name": "DenyInternetToSfYarp",
        "protocol": "tcp",
        "sourcePortRange": "*",
        "sourceAddressPrefix": "Internet",
        "destinationAddressPrefix": "*",
        "destinationPortRange": "443",
        "access": "Deny",
        "priority": 2100,
        "direction": "Inbound"
      }
    ]
  }
}
```

Replace `10.0.1.0/24` with your Application Gateway subnet's CIDR.

The rules above target the post-cutover state where SFYarp listens on
`443`. During coexistence with the built-in proxy (see
[§5.1](#51-deploy-sfyarp-on-your-sfrp-cluster) step 2), substitute
your chosen HTTPS port (for example `8443`) for both
`destinationPortRange` values.

If your L7 terminates TLS and forwards plain HTTP to SFYarp (see the
callout at the top of [§8](#8-tls-and-certificate-management)), use
`destinationPortRange: "80"` instead.

For **Front Door** as the edge instead of Application Gateway, use
`sourceAddressPrefix: AzureFrontDoor.Backend` in the allow rule.
Front Door reuses the backend port for its health probes (probes
cannot be configured to use a separate port), so no additional NSG
rule is needed.

For **internal-only** ingress (no public IP), keep SFYarp behind an
internal Azure Load Balancer and use `sourceAddressPrefix:
VirtualNetwork` (or specific VNet CIDRs) — service tags aren't
required and the deny-Internet rule is unnecessary.

---

## 11. Limitations and behavior changes

**Platform limits** — check these first:

- **Windows-only.** No Linux support.
- **HTTP/HTTPS only.** No TCP proxying.

**Caller-visible behavior changes** — existing clients will notice:

- **Services without labels return 404** instead of being
  auto-exposed. See [§5.2](#52-opt-in-a-service-with-labels).
- **`PartitionID=<guid>` only** ([§6](#6-client-url-translation)). No
  `PartitionKind` + `PartitionKey` translation at the proxy. Callers
  resolve the partition GUID once and cache per key.
- **`X-ServiceFabric` response header is not emitted**
  ([§6.5](#65-response-headers)). Clients that switch on
  `NoRetry` / `ResourceNotFound` must move to HTTP status codes.
- **No implicit retry on 404.** The built-in proxy re-resolves service
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

**Not affected by the migration** — these keep working unchanged:

- **SF Remoting** (`FabricTransport` / V2 remoting). Clients resolve
  endpoints through the SF Naming Service, not through any HTTP
  reverse proxy. Turning off the built-in reverse proxy has no
  impact on remoting traffic.
- **Direct Naming Service resolution** used by any code path that
  reaches the backend without going through the built-in reverse
  proxy (for example, `ServicePartitionResolver` in the SF client
  SDK).

---

## 12. Rollback plan

Rollback depends on how far you've progressed:

- **After Step 1 (SFRP + SFYarp coexisting).** Flip DNS or the LB back
  to the built-in reverse proxy port. You can delete
  `fabric:/YarpProxyApp` at leisure. Service-manifest labels added in
  [§5.2](#52-opt-in-a-service-with-labels) are inert without SFYarp
  routing — no need to revert them.
- **After Step 2 (built-in disabled on SFRP).** Re-enable the built-in
  reverse proxy through a cluster-manifest upgrade: set
  `ApplicationGateway/Http/IsEnabled=true` and re-add the reverse-proxy
  endpoint to each node type. A cluster-manifest upgrade rolls out over
  roughly one UD cycle.
- **After Step 3 (moved to SFMC).** You cannot roll back to the
  built-in reverse proxy on SFMC — it does not exist there. The
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
[§5.1a](#51a-deploy-sfyarp-via-arm-recommended) step 4. This is why
keeping the previous `applicationTypes/versions` resource and its
SFPKG blob for at least one rollback window matters.

---

## 13. Troubleshooting

Start every SFYarp investigation by collecting logs. The symptom table
in [§13.2](#132-common-symptoms-and-fixes) assumes you can see what SFYarp is
actually doing on the node.

### 13.1 Collecting SFYarp logs

There are two complementary ways to get diagnostics out of SFYarp,
in roughly the order you should reach for them:

**1. Windows Event Viewer (quick per-node check).**
SFYarp writes structured events to the Windows Event Log under the
event source **`YarpProxyLogs`**. On any node running the proxy:

- Open Event Viewer → *Applications and Services Logs* → *YarpProxyLogs*
  (or run `Get-WinEvent -LogName 'YarpProxyLogs'` from an elevated
  PowerShell prompt).
- Filter by time and severity to spot startup errors, cert-load
  failures, and route-config problems.
- **If `YarpProxyLogs` has nothing yet** (SFYarp failed before its
  own logging came up), check the SF Hosting log
  **`Microsoft-ServiceFabric/Admin`** for `ApplicationProcessExited`
  events, which record the process `ExitCode`.
  See [§13.2](#132-common-symptoms-and-fixes) for common
  startup-failure signatures.

This is useful when you're already RDP'd or live-siting a node and
just need to know "did this instance come up cleanly?"

**2. Console redirection to per-node log files.**
If you need the raw stdout / stderr from `YarpProxy.Service` (for
example, when reproducing a crash locally), enable **console
redirection** in the SFYarp service manifest. Service Fabric will write the process's console output to a
file under the application's work directory on each node — see
[Set up logging: existing app](https://learn.microsoft.com/azure/service-fabric/service-fabric-deploy-existing-app#set-up-logging).

Edit `YarpProxy.Service`'s `ServiceManifest.xml` and wrap the
`ExeHost` code package with a `ConsoleRedirection` policy — for
example:

```xml
<CodePackage Name="Code" Version="...">
  <EntryPoint>
    <ExeHost>
      <Program>YarpProxy.Service.exe</Program>
      <ConsoleRedirection FileRetentionCount="5" FileMaxSizeInKb="2048"/>
    </ExeHost>
  </EntryPoint>
</CodePackage>
```

Then upgrade the SFYarp application. Log files land under the
node's SFYarp application work directory (see the link above for the
exact path). **Console redirection is a development / diagnostic
tool, not a production setting.**

**For centralized logging across a fleet**, treat SFYarp like any
other Service Fabric service: forward the `YarpProxyLogs` event
source (and the SF Hosting log) through the SF diagnostic extension
to Azure Monitor / Log Analytics, or your existing telemetry
pipeline.

### 13.2 Common symptoms and fixes

| Symptom | Likely cause | Where to look |
|---|---|---|
| SFMC nodetype deploy fails with `LinkedAuthorizationFailed` naming `Microsoft.ManagedIdentity/userAssignedIdentities/assign/action` on your user-assigned managed identity (UAMI) | The Service Fabric RP hasn't been granted `Managed Identity Operator` on the UAMI, so it can't attach the identity to the managed VMSS | Grant the SFMC RP the `Managed Identity Operator` role on the UAMI per the [SFMC managed-identity guide](https://learn.microsoft.com/en-us/azure/service-fabric/how-to-managed-identity-managed-cluster-virtual-machine-scale-sets), then retry |
| 404 from SFYarp for a service that used to work through the built-in reverse proxy | Service was not opted in | Target service's `ServiceManifest.xml` `<Extensions>` block; [§5.2](#52-opt-in-a-service-with-labels) |
| SFYarp returns 404 for every path against a known-good backend | Client URL is missing the `/{AppName}/{ServiceName}/` prefix, or uses the built-in reverse proxy's `?PartitionKey=&PartitionKind=` query params which SFYarp doesn't honor | Rewrite the URL per [§6](#6-client-url-translation); for stateful backends use `?PartitionID=<guid>` per [§6.2](#62-stateful-int64-or-named) |
| 502 Bad Gateway right after deploy | `AllowInsecureHttp=false` set on the route while the backend publishes an HTTP-only endpoint | Move the backend to HTTPS, or (dev/test only) drop the explicit `false` — SFYarp 2.x defaults to `true` |
| TLS handshake fails on the HTTPS port; Windows System event log shows `Schannel` **Event 36870** with `0x8009030D` naming `YarpProxy.Service`, and `YarpProxyLogs` shows an `AuthenticationException` referencing the certificate thumbprint (with a matching private-key-resolution error in the Windows Application log) | Private-key ACL isn't set — Service Fabric grants private-key access only when the certificate is declared as `<EndpointCertificate>` in `ApplicationManifest.xml` | Add `<EndpointCertificate FindBySubjectName>` per [§8.4](#84-private-key-acls-for-sfyarp); confirm `KVVMExtension` has `setupOrder: ["BeforeSFRuntime"]` + `requireInitialSync: true` per [§8.3](#83-deploying-certificates-to-nodes). Manual `Set-Acl` against `%ProgramData%\Microsoft\Crypto\Keys` is a last resort and does not survive rotation |
| TLS handshake succeeds but wrong certificate is served | Multiple certificates in `LocalMachine\My` — SNI picks by SAN | Ensure only the intended certificate has a matching SAN; [§8.2](#82-certificate-naming-sancn) |
| `YarpProxy.Service` won't start; port bind fails | Port already in use (built-in reverse proxy still on the same port) | Move SFYarp to a coexistence port during Step 1 |
| `YarpProxy.Service` crash-loops with `RemoteConfigWorker … abort timeout` in `YarpProxyLogs`, or a `.NET Runtime` crash referencing `RemoteConfigWorker` in the Application event log | Application URI doesn't match `fabric:/YarpProxyApp`, which SFYarp uses to discover `FabricDiscovery.Service` | Redeploy the application as `fabric:/YarpProxyApp` (see [§5.1a](#51a-deploy-sfyarp-via-arm-recommended)) |
| `FabricDiscovery.Service` restarts repeatedly | `AbortAfterConsecutiveFailures` reached | Check Naming Service health; raise `AbortAfterTimeoutInSeconds` |
| Requests to a stateful service occasionally hit the wrong partition | Client isn't passing `PartitionID` | Client must pass `?PartitionID=<guid>`; [§6](#6-client-url-translation) |
| Requests to a stateful service go to secondaries when you wanted primaries | Missing `StatefulReplicaSelectionMode=PrimaryOnly` label | Add the label; [§5.2](#52-opt-in-a-service-with-labels) |
| Post-upgrade 502s from the L7 in front / Application Gateway marks the ingress backend pool unhealthy | L7 probe points at a business path that's now cold | Point the probe at a dedicated `/proxy-health` route; [§9.1](#91-health-probes) |
| A label appears to be ignored | Typo or case inconsistency (`Healthcheck` vs `HealthCheck`) | Prefer camel-case `HealthCheck`; cross-check against [Appendix](#appendix--sfyarp-labels-reference) |
| Newly-added nodes serve TLS errors for a few minutes after scale-out (SFMC) | KV VM extension hasn't finished installing certificates yet | Application must retry on cold start; [§8.5](#85-extension-ordering-race-on-sfmc-scale-out) |
| SFYarp still serves the old certificate after a Key Vault rotation | The new certificate version hasn't propagated to the nodes yet, or SFYarp hasn't rescanned `LocalMachine\My` yet | [§8.7](#87-certificate-rotation) |
| Backend rejects requests because `Host` header is the cluster FQDN, not the app hostname | SFYarp is not preserving original `Host` | Add `RequestHeaderOriginalHost=true` transform; [§8.6](#86-in-cluster-service-to-service-traffic) |
| Client relied on `X-ServiceFabric` header | SFYarp doesn't emit it | Update client to switch on HTTP status code / body |

---

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
See [§8.2](#82-certificate-naming-sancn) for cert-naming details.

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
| `Yarp.Backend.HealthCheck.Active.Enabled` / `.Path` / `.Interval` / `.Timeout` | Proxy → backend active health checks (see [§5.2](#52-opt-in-a-service-with-labels)) |
| `Yarp.Backend.HttpClient.SslProtocols` | e.g. `Tls12,Tls13` when the backend requires it |
| `Yarp.Backend.AllowInsecureHttp` | Allow SFYarp to forward to a plain-HTTP backend. Current default is `true` — set to `false` explicitly on every production route |
| `Yarp.Routes.<name>.Transforms.[N].*` | Header add / remove / rename, path transforms |

**For the complete label catalog** — route matching, backend behavior,
session affinity, load balancing, metadata, and every optional
parameter — see the SFYarp repo's
[Supported Labels](https://github.com/microsoft/service-fabric-yarp#supported-labels)
section. That page is the source of truth and is updated with each
release. This appendix intentionally covers only the migration
essentials to avoid drift.

For the underlying YARP concepts (transforms, session affinity,
health-check policies), see the
[YARP documentation](https://github.com/dotnet/yarp).
