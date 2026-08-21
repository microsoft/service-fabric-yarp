# SFMC starter ARM template for SFYarp 2.0.0

This sample deploys SFYarp 2.0.0 onto a fresh Service Fabric managed
cluster (SFMC) in a single ARM deployment. It bundles:

- **The managed cluster** (`Microsoft.ServiceFabric/managedclusters`)
  with load-balancing rules for the SFYarp HTTP and HTTPS ports.
- **A primary node type** with the **Key Vault VM extension**
  (`KeyVaultForWindows`) ordered with `setupOrder: ["BeforeSFRuntime"]`
  and `requireInitialSync: true` so the endpoint TLS certificate is
  installed on every node *before* Service Fabric starts. This is
  covered in [§8.3 / §8.5 of the migration guide][migration-guide].
- **The SFYarp application** as three linked ARM resources:
  `applicationTypes`, `applicationTypes/versions`, and `applications`.
  Application name is fixed to `fabric:/YarpProxyApp`.

The template is intentionally minimal — one node type, no L7 gateway,
no custom NSG rules. Real deployments will layer on ingress
(Application Gateway or Front Door), NSG lockdown, and additional
node types as described in the migration guide.

## What's not in this template

- **No CustomScriptExtension to install .NET.** SFYarp 2.0.0 ships
  self-contained on .NET 10, so no runtime install is required.
- **No L7 gateway.** See [§9 of the migration guide][migration-guide-9]
  for App Gateway / Front Door in front of SFYarp.
- **No NSG lockdown rules.** See [§10 of the migration
  guide][migration-guide-10] for `networkSecurityRules` under the
  managed cluster resource.
- **No `<EndpointCertificate>` declaration in
  `ApplicationManifest.xml`.** The template installs the cert on the
  node via the KV VM extension. To enable private-key ACLs for the
  SFYarp process account, edit `YarpProxyApp\ApplicationManifest.xml`
  before re-packaging the sfpkg — see [§8.4 of the migration
  guide][migration-guide-84].

## Prerequisites

Before running the deployment:

1. **A Key Vault** with the endpoint TLS certificate imported as a
   Key Vault certificate (which surfaces as a secret).
2. **A user-assigned managed identity** with the **Key Vault Secrets
   User** role on the Key Vault. Its resource ID goes into
   `uamiResourceId`.
3. **A cluster admin client certificate** — thumbprint goes into
   `clientCertificateThumbprint`.
4. **The SFYarp `.sfpkg`** uploaded to a blob the cluster can reach.
   Keep the version in the blob path
   (e.g. `sfpkgs/YarpProxyApp_2.0.0.sfpkg`) so previous versions stay
   reachable for rollback. See [§5.1a step 2 of the migration
   guide][migration-guide-51a] for how to repackage the shipped
   `YarpProxyApp` folder as an sfpkg.

## Parameters

Fill in `cluster.parameters.example.json`, save it as
`cluster.parameters.json`, then deploy:

```powershell
az deployment group create `
  --resource-group <your-rg> `
  --template-file cluster.json `
  --parameters @cluster.parameters.json `
  --parameters adminPassword=<secure-password>
```

Key parameters:

| Parameter | Meaning |
|---|---|
| `certUrlVersionless` | Versionless KV secret URL for the endpoint TLS cert. The KV VM extension installs this into `LocalMachine\My` on every node. Versionless lets rotation flow through without ARM edits. |
| `endpointCertSubject` | Subject match for SNI. SFYarp scans `LocalMachine\My` and picks the cert whose subject matches, so this survives certificate rotation. |
| `endpointCertThumbprint` | Optional thumbprint fallback. Leave empty when using `endpointCertSubject`. |
| `yarpSfpkgUrl` | HTTPS URL of the `YarpProxyApp_2.0.0.sfpkg` blob. |
| `yarpAppTypeVersion` | Must match the version inside the sfpkg's `ApplicationManifest.xml`. Default `2.0.0`. |
| `yarpHttpPort` / `yarpHttpsPort` | Ports SFYarp listens on. Load-balancing rules and probes use the same ports. |
| `yarpInstanceCount` | `-1` for one instance per eligible node. |
| `uamiResourceId` | UAMI with `Key Vault Secrets User` on the KV. |

## Upgrading SFYarp later

To upgrade to a new SFYarp version, follow [§5.1a step 4 of the
migration guide][migration-guide-51a]:

1. Upload the new sfpkg to a **new** version-in-path blob (don't
   overwrite the previous one).
2. Add a second `applicationTypes/versions` resource pointing at the
   new blob.
3. Flip the `applications` resource's `version` property to the new
   `versions` resource ID.

Rollback is the same flip back to the previous `versions` resource ID
— which is why version-in-path storage matters.

## See also

- [Migration guide: Reverse Proxy → SFYarp][migration-guide]
- [SFMC application secrets docs](https://learn.microsoft.com/azure/service-fabric/how-to-managed-cluster-application-secrets)
- [SFMC networking / NSG rules](https://learn.microsoft.com/azure/service-fabric/how-to-managed-cluster-networking)

[migration-guide]: ../../migrate-from-sf-reverse-proxy-to-sfyarp.md
[migration-guide-9]: ../../migrate-from-sf-reverse-proxy-to-sfyarp.md#9-l7-gateway-in-front-of-sfyarp
[migration-guide-10]: ../../migrate-from-sf-reverse-proxy-to-sfyarp.md#10-network-lockdown-nsg-and-service-tags
[migration-guide-84]: ../../migrate-from-sf-reverse-proxy-to-sfyarp.md#84-private-key-acls-for-sfyarp
[migration-guide-51a]: ../../migrate-from-sf-reverse-proxy-to-sfyarp.md#51a-deploy-sfyarp-via-arm-recommended
