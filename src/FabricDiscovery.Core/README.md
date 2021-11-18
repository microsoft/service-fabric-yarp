# FabricDiscovery.Core overview

This project does the heavy lifting of discovering the topology of the Service Fabric cluster
(all apps, services, partitions, etc.) and converting them to appropriate YARP configuration model objects
that represent the desired ingress configuration.

Processing is performed as an asynchronous pipeline, where each stage produces some data that is consumed by the next stage
via a change notification mechanism. Notifications are _pushed_ down the pipeline (looking at the diagram below),
which prompts each stage to _pull_ data from the preceding stage.


### High level data flow

```
TopologyDiscoveryWorker
    │
    │  IReadOnlyDictionary<ApplicationNameKey, DiscoveredApp>
    ▼
SFYarpTopologyMapperWorker
    │
    │  IReadOnlyList<SFYarpBackendService>
    ▼ 
SFYarpConfigProducerWorker
    │
    │  Yarp.ReverseProxy.IProxyConfig
    ▼
SFYarpConfigSerializerWorker
    │
    │  SFYarpSerializedConfig
    ▼
API controller (consumed by YarpProxy service instances)
```
