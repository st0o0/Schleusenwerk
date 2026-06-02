# Changelog

## [0.4.0](https://github.com/st0o0/Schleusenwerk/compare/v0.3.0...v0.4.0) (2026-06-02)


### Features

* **AppHost:** add Aspire AppHost configuration ([aa49eb1](https://github.com/st0o0/Schleusenwerk/commit/aa49eb11d519e24234dfcfaf10209000d18b431a))
* **AppHost:** add OpenTelemetry instrumentation ([1451085](https://github.com/st0o0/Schleusenwerk/commit/145108527c7d3c0aa9ed3e108222ce16530f37ad))
* **ci:** consolidate and optimize workflows ([e530a33](https://github.com/st0o0/Schleusenwerk/commit/e530a3370585acdd6544267f96b5ea157b573231))
* **Startup:** add DOMAINS env var integration tests ([4afeefa](https://github.com/st0o0/Schleusenwerk/commit/4afeefa855e7b851a79bd278355b118a64a3f289))


### Bug Fixes

* **AppHost:** use RunAsync and clean up config ([9dabe94](https://github.com/st0o0/Schleusenwerk/commit/9dabe947c8cb2dfa239e84ae0ae9078ea3afcc97))

## [0.3.0](https://github.com/st0o0/Schleusenwerk/compare/v0.2.1...v0.3.0) (2026-06-02)


### Features

* **actor:** improve actor context usage ([074c5d7](https://github.com/st0o0/Schleusenwerk/commit/074c5d7eec9eb5d4568afe5b3424a4ec64f24dd7))
* add configurable Kestrel resource limits for production hardening ([c588f5b](https://github.com/st0o0/Schleusenwerk/commit/c588f5bbe75b5168e11191a784e8d429de1bfd85))
* add ConnectionTracker middleware for graceful drain ([f3081ba](https://github.com/st0o0/Schleusenwerk/commit/f3081ba5d03bf044a59a6a9f5178e8cff44da765))
* add GracefulShutdownService with configurable drain timeout ([fc5ac54](https://github.com/st0o0/Schleusenwerk/commit/fc5ac540da2a808dc19c853bced9b4b64fe313f3))
* add Serilog logging with configurable sinks ([5288834](https://github.com/st0o0/Schleusenwerk/commit/5288834c12ca65a83234ba0e7dedecc143386cf9))
* add structured access logging middleware with Serilog ([ea782bd](https://github.com/st0o0/Schleusenwerk/commit/ea782bdd5a71149f2843ec03aadc08a35957edb0))
* enhance domain name validation and test suite ([b2efee0](https://github.com/st0o0/Schleusenwerk/commit/b2efee0947e77f88ee92b0d0c060fdce693e0856))
* record circuit breaker trip metrics on state transition ([6ff6306](https://github.com/st0o0/Schleusenwerk/commit/6ff63061b95982a7ca349a1e605b383e94cb5ecb))
* replace TurboHTTP with standard HttpClient for upstream forwarding ([9d45d79](https://github.com/st0o0/Schleusenwerk/commit/9d45d796adefafe9b7016d293698e38f2e11f713))
* **tests:** add certificate provisioning gap coverage tests ([94a39af](https://github.com/st0o0/Schleusenwerk/commit/94a39af0dbed6fd1e68aa1535f3b596a1d91b163))
* **tests:** add Docker auto-registration integration tests ([b20aa14](https://github.com/st0o0/Schleusenwerk/commit/b20aa14838474253b8e8432c7cb45c995fc1ed99))
* **tests:** add DockerAvailableGuard for conditional test skipping ([2ff4864](https://github.com/st0o0/Schleusenwerk/commit/2ff4864d6fefe8335f0b387ae615b7582d6b9a5b))
* **tests:** add DockerDiscoveryTestHost with Docker enabled ([bffce9b](https://github.com/st0o0/Schleusenwerk/commit/bffce9bf76b41c7923035cd2c028b5485da3dc09))
* **tests:** add Resilience, Certificates, Api, Events integration specs ([3f6b82a](https://github.com/st0o0/Schleusenwerk/commit/3f6b82a464d4da57f7f0dc6c0aa807330c7f6bb0))
* **tests:** add Routing and Forwarding integration specs ([bca57de](https://github.com/st0o0/Schleusenwerk/commit/bca57deed01f272d2d9804c8bd7903773d1a922d))
* **tests:** add SchleusenwerkTestHost in-process fixture ([2a24bf9](https://github.com/st0o0/Schleusenwerk/commit/2a24bf9a4bad25ef275f2dd494b9bacb3e4eb10c))
* **tests:** add TestContainer fixtures and TestHelper ([fad54da](https://github.com/st0o0/Schleusenwerk/commit/fad54dadffcf83e4ef49047adec6ac98421062e4))
* **tests:** add Toxiproxy edge case tests for bandwidth, truncation, and recovery ([2306048](https://github.com/st0o0/Schleusenwerk/commit/23060480fb546bd0b6572d9cf69c74b53d63a109))
* **tests:** replace Toxiproxy.Net with direct REST API client ([d613377](https://github.com/st0o0/Schleusenwerk/commit/d6133779758ffc7db160ee3d714376d8fb4cf329))


### Bug Fixes

* docker event deserialization and route deletion timing ([c8401b8](https://github.com/st0o0/Schleusenwerk/commit/c8401b8cd7c005c5823ef7248f6d8de580909648))
* migrate to Docker.DotNet.Enhanced and fix actor threading bug ([9808602](https://github.com/st0o0/Schleusenwerk/commit/9808602f074ec0c58e1bfc8041d6e1cc48739e50))
* resolve EventBridgeService stream compilation error ([7bf4072](https://github.com/st0o0/Schleusenwerk/commit/7bf40727e0f65e3eb45bc728bb94961e7b11f134))
* resolve route-loss and EventBridge race condition showstoppers ([6cfb327](https://github.com/st0o0/Schleusenwerk/commit/6cfb3276d0353e75b6e0f368c86b974abdbf57fe))
* **tests:** fix all integration test assertions ([34fcebe](https://github.com/st0o0/Schleusenwerk/commit/34fcebecd0bb4bf21cf5899c87a3e68419e7ba79))
* **tests:** fix CertificateProvisioningActorSpec for CI stability ([c69b151](https://github.com/st0o0/Schleusenwerk/commit/c69b151e9c7e75431b0efa1afb6b6aaed1ab2b55))
* **tests:** fix TestHost startup — port binding, controller discovery, Docker disable ([afa9f12](https://github.com/st0o0/Schleusenwerk/commit/afa9f1279f3eeb5bddc2b4f54c470b3cd7dcefc1))

## [0.2.1](https://github.com/st0o0/Schleusenwerk/compare/v0.2.0...v0.2.1) (2026-05-04)


### Bug Fixes

* **docker:** reduce image size ([b841912](https://github.com/st0o0/Schleusenwerk/commit/b8419122c05e1ae4e6194bbabf1f8eec1b324d6f))

## [0.2.0](https://github.com/st0o0/Schleusenwerk/compare/v0.1.0...v0.2.0) (2026-05-04)


### Features

* add initial feature scaffolding ([d33d3e9](https://github.com/st0o0/Schleusenwerk/commit/d33d3e96b6da20e84ee060561578cd48634c0ee4))
* **aspire:** add .NET Aspire orchestration ([9b633c4](https://github.com/st0o0/Schleusenwerk/commit/9b633c431f4e52040af926f4441d9908eec54089))
* **certs:** add Lego ACME certificate provider ([785885f](https://github.com/st0o0/Schleusenwerk/commit/785885fd6088766e7308f40394600337135d645b))
* **discovery:** implement DockerDiscoveryActor with container label parsing ([d17e6ba](https://github.com/st0o0/Schleusenwerk/commit/d17e6ba33b8c456d0f02f897c371c196c30513c5))
* **docker:** add docker-compose configuration ([8e18fdc](https://github.com/st0o0/Schleusenwerk/commit/8e18fdca42d6b15d52d72abe661233e3dd22935a))
* **docker:** create multi-stage Dockerfiles for proxy and UI ([95f2064](https://github.com/st0o0/Schleusenwerk/commit/95f206486d85e1b68a28f7bd30bf02907ef5539f))
* **docker:** set up complete Docker release infrastructure ([cbef9ed](https://github.com/st0o0/Schleusenwerk/commit/cbef9eda12cbb69799c428b45a369bad95c8dee7))
* implement initial task structure ([9b51c36](https://github.com/st0o0/Schleusenwerk/commit/9b51c366dba2f5cd45bf4281b65bc97076c6381e))
* **proxy:** add proxy forwarding endpoint ([825a791](https://github.com/st0o0/Schleusenwerk/commit/825a7911f59edce9feaefe266c0c41871c756ea4))
* **release:** implement semantic release workflow with release-please ([d5b5e92](https://github.com/st0o0/Schleusenwerk/commit/d5b5e924c5842a55f7e6b2b7e0b77e65a5c7dfa9))
* **routes:** add WebSocket configuration per domain ([2c86b80](https://github.com/st0o0/Schleusenwerk/commit/2c86b80c87c9756fc3dca5cbc2289c2217c9835d))
* Schleusenwerk MVP ([7cc06d8](https://github.com/st0o0/Schleusenwerk/commit/7cc06d8dbd288ed2ec4b7b54d5f962db7a85de28))
* **tls:** add TLS certificate management ([bf82e6f](https://github.com/st0o0/Schleusenwerk/commit/bf82e6f476bd1f75b5afaa944448e4f20951b354))
* **tls:** implement Kestrel port configuration with self-signed HTTPS ([3e3d46f](https://github.com/st0o0/Schleusenwerk/commit/3e3d46f2f7f960562e18faa9eef471fbac40f09e))
* **ui:** add management UI with gRPC ([d2bed7a](https://github.com/st0o0/Schleusenwerk/commit/d2bed7a7594e57ce6819cbe435fc51cd6fa21f37))
* **ui:** add Vue.js SPA with PrimeVue ([8a27d0f](https://github.com/st0o0/Schleusenwerk/commit/8a27d0f56c6d4fe02aa07bcec57822edd87d9cdc))


### Bug Fixes

* minor adjustments ([3336a35](https://github.com/st0o0/Schleusenwerk/commit/3336a3552e8505db234885cc8ceb84c8e2b699f8))
* resolve build warnings (SYSLIB0057, NU1902, xUnit1051) ([f2237f2](https://github.com/st0o0/Schleusenwerk/commit/f2237f22e494e007e765a26b3663b46c081a5f21))
