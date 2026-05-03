# Schleusenwerk Feature-Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the gap to https-portal feature parity with four features: Docker Discovery REST endpoint, custom certificate upload, production docker-compose, and ACME/Let's Encrypt with auto-renewal.

**Architecture:** Each feature is independent. Discovery endpoint adds a query message to the existing `DockerDiscoveryActor` + a new controller. Cert upload extends `CertificateController` with multipart form handling. ACME extends `CertificateProvisioningActor` with Certes integration, a challenge store singleton, and an HTTP-01 challenge endpoint. Production compose removes port 5000 from host mappings.

**Tech Stack:** ASP.NET Core Controllers, Akka.NET Ask pattern, Certes (ACME), X509Certificate2, Vue 3 + Pinia

**Spec:** `docs/superpowers/specs/2026-05-03-feature-parity-design.md`

---

## File Map

### New files
- `src/Schleusenwerk/Discovery/DiscoveryMessages.cs` — Query message + result type for DockerDiscoveryActor
- `src/Schleusenwerk/Controllers/DiscoveryController.cs` — REST endpoint for container discovery
- `src/Schleusenwerk/Certificates/IAcmeClient.cs` — ACME client interface (testability)
- `src/Schleusenwerk/Certificates/CertesAcmeClient.cs` — Certes implementation
- `src/Schleusenwerk/Certificates/AcmeChallengeStore.cs` — In-memory challenge token store
- `src/Schleusenwerk.Web/src/stores/discovery.ts` — Pinia store for Hafenbecken
- `docker-compose.prod.yml` — Production compose without port 5000

### Modified files
- `src/Schleusenwerk/Discovery/DockerDiscoveryActor.cs` — Add query handler, expand tracked state
- `src/Schleusenwerk/Controllers/CertificateController.cs` — Add upload endpoint
- `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs` — ACME flow + auto-renewal
- `src/Schleusenwerk/Api/ApiModels.cs` — Add DiscoveredContainerDto
- `src/Schleusenwerk/Api/DomainModelMapper.cs` — Add CertificateProvisioningFailed event mapping
- `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs` — Register AcmeChallengeStore, IAcmeClient
- `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs` — Add ACME challenge endpoint
- `src/Schleusenwerk.Core/Persistence/ProxySettings.cs` — Add AcmeEmail field
- `src/Schleusenwerk.Contracts/openapi.yaml` — Add discovery + upload endpoints
- `src/Schleusenwerk.Web/src/api/client.ts` — Add discovery + upload API methods
- `src/Schleusenwerk.Web/src/pages/Hafenbecken.vue` — Replace placeholder with real UI
- `src/Schleusenwerk.Web/src/pages/Siegel.vue` — Add upload button

---

## Task 1: Discovery Messages + Actor Query Handler

**Files:**
- Create: `src/Schleusenwerk/Discovery/DiscoveryMessages.cs`
- Modify: `src/Schleusenwerk/Discovery/DockerDiscoveryActor.cs`

- [ ] **Step 1: Create DiscoveryMessages.cs with query/result types**

Create `src/Schleusenwerk/Discovery/DiscoveryMessages.cs`:

```csharp
using Schleusenwerk.Routing;

namespace Schleusenwerk.Discovery;

public sealed record GetDiscoveredContainers
{
    public static readonly GetDiscoveredContainers Instance = new();
}

public sealed record TrackedContainer(
    string ContainerId,
    string Name,
    string Image,
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    DomainName? AssignedDomain,
    UpstreamUrl? AssignedUrl);

public sealed record DiscoveredContainersResult(IReadOnlyList<TrackedContainer> Containers);
```

- [ ] **Step 2: Expand tracked state in DockerDiscoveryActor**

In `src/Schleusenwerk/Discovery/DockerDiscoveryActor.cs`, replace the `_tracked` field:

```csharp
    private readonly Dictionary<string, (DomainName Domain, UpstreamUrl Url)> _tracked = new();
```

With:

```csharp
    private readonly Dictionary<string, TrackedContainer> _tracked = new();
```

- [ ] **Step 3: Add Receive handler for GetDiscoveredContainers in the constructor**

In the constructor, after the existing `Receive<Noop>(_ => { });` line, add:

```csharp
        Receive<GetDiscoveredContainers>(_ =>
            Sender.Tell(new DiscoveredContainersResult(_tracked.Values.ToList())));
```

- [ ] **Step 4: Update RegisterContainerIfLabeled to store full container info**

Replace the line `_tracked[containerId] = (parsed.Domain, parsed.Upstream.Url);` and update the method signature. The method currently receives `(string containerId, IDictionary<string, string> labels, string? ip)`. We need to also pass name, image, status. 

First, update the `Handle(ScanResult)` method — replace:

```csharp
    private void Handle(ScanResult msg)
    {
        foreach (var container in msg.Containers)
        {
            var ip = ExtractIp(container.NetworkSettings?.Networks);
            var labels = container.Labels ?? new Dictionary<string, string>();
            RegisterContainerIfLabeled(container.ID, labels, ip);
        }

        StartMonitoring();
    }
```

With:

```csharp
    private void Handle(ScanResult msg)
    {
        foreach (var container in msg.Containers)
        {
            var ip = ExtractIp(container.NetworkSettings?.Networks);
            var labels = container.Labels ?? new Dictionary<string, string>();
            RegisterContainerIfLabeled(
                container.ID,
                container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12],
                container.Image ?? "",
                container.State ?? "",
                labels,
                ip);
        }

        StartMonitoring();
    }
```

Then update `Handle(ContainerInspected)` — replace:

```csharp
    private void Handle(ContainerInspected msg)
    {
        var ip = ExtractIp(msg.Response.NetworkSettings.Networks);
        var labels = msg.Response.Config.Labels ?? new Dictionary<string, string>();
        RegisterContainerIfLabeled(msg.Response.ID, labels, ip);
    }
```

With:

```csharp
    private void Handle(ContainerInspected msg)
    {
        var ip = ExtractIp(msg.Response.NetworkSettings.Networks);
        var labels = msg.Response.Config.Labels ?? new Dictionary<string, string>();
        RegisterContainerIfLabeled(
            msg.Response.ID,
            msg.Response.Name?.TrimStart('/') ?? msg.Response.ID[..12],
            msg.Response.Config.Image ?? "",
            msg.Response.State?.Status ?? "",
            labels,
            ip);
    }
```

Then replace the entire `RegisterContainerIfLabeled` method:

```csharp
    private void RegisterContainerIfLabeled(
        string containerId, string name, string image, string status,
        IDictionary<string, string> labels, string? ip)
    {
        if (!labels.ContainsKey("schleusenwerk.domain"))
        {
            _tracked[containerId] = new TrackedContainer(
                containerId, name, image, status,
                new Dictionary<string, string>(labels), null, null);
            return;
        }

        if (!ContainerLabelParser.TryParse(labels, ip, out var parsed, out var error))
        {
            _log.Warning("Skipping container {Id}: {Error}", containerId[..12], error);
            _tracked[containerId] = new TrackedContainer(
                containerId, name, image, status,
                new Dictionary<string, string>(labels), null, null);
            return;
        }

        _tracked[containerId] = new TrackedContainer(
            containerId, name, image, status,
            new Dictionary<string, string>(labels), parsed.Domain, parsed.Upstream.Url);

        var domainConfig = new DomainConfig
        {
            DomainName = parsed.Domain,
            ForceHttps = true,
        };

        _domainRegion.Tell(new AddDomain(domainConfig));
        _domainRegion.Tell(new AddUpstream(parsed.Domain, parsed.Upstream));

        _log.Info("Registered container {Id} → {Domain} @ {Url}", containerId[..12], parsed.Domain, parsed.Upstream.Url);
    }
```

- [ ] **Step 5: Update UnregisterContainer to use new tracked type**

Replace:

```csharp
    private void UnregisterContainer(string containerId)
    {
        if (!_tracked.Remove(containerId, out var entry))
        {
            return;
        }

        _domainRegion.Tell(new RemoveUpstream(entry.Domain, entry.Url));

        _log.Info("Unregistered container {Id} upstream {Url}", containerId[..12], entry.Url);
    }
```

With:

```csharp
    private void UnregisterContainer(string containerId)
    {
        if (!_tracked.Remove(containerId, out var entry))
        {
            return;
        }

        if (entry.AssignedDomain is not null && entry.AssignedUrl is not null)
        {
            _domainRegion.Tell(new RemoveUpstream(entry.AssignedDomain.Value, entry.AssignedUrl.Value));
            _log.Info("Unregistered container {Id} upstream {Url}", containerId[..12], entry.AssignedUrl);
        }
    }
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Schleusenwerk/Discovery/
git commit -m "feat(discovery): add GetDiscoveredContainers query + expand tracked container state"
```

---

## Task 2: Discovery REST Controller + API Models

**Files:**
- Create: `src/Schleusenwerk/Controllers/DiscoveryController.cs`
- Modify: `src/Schleusenwerk/Api/ApiModels.cs`
- Modify: `src/Schleusenwerk.Contracts/openapi.yaml`

- [ ] **Step 1: Add DiscoveredContainerDto to ApiModels.cs**

Add at the end of `src/Schleusenwerk/Api/ApiModels.cs`:

```csharp
public sealed record DiscoveredContainerDto(
    string Name,
    string Image,
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    string? AssignedDomain,
    string? ConflictReason);
```

- [ ] **Step 2: Create DiscoveryController.cs**

Create `src/Schleusenwerk/Controllers/DiscoveryController.cs`:

```csharp
using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Discovery;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/discovery")]
internal sealed class DiscoveryController : ControllerBase
{
    private readonly IActorRef _discoveryActor;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public DiscoveryController(IReadOnlyActorRegistry registry)
    {
        _discoveryActor = registry.Get<DockerDiscoveryActor>();
    }

    [HttpGet("containers")]
    public async Task<ActionResult<IReadOnlyList<DiscoveredContainerDto>>> ListContainers(CancellationToken ct)
    {
        var result = await _discoveryActor.Ask<DiscoveredContainersResult>(
            GetDiscoveredContainers.Instance, _timeout, ct);

        var dtos = result.Containers.Select(c => new DiscoveredContainerDto(
            Name: c.Name,
            Image: c.Image,
            Status: c.Status,
            Labels: c.Labels,
            AssignedDomain: c.AssignedDomain?.Value,
            ConflictReason: null)).ToList();

        return Ok(dtos);
    }
}
```

- [ ] **Step 3: Add discovery endpoint to openapi.yaml**

Add before the `components:` section in `src/Schleusenwerk.Contracts/openapi.yaml`:

```yaml
  /api/discovery/containers:
    get:
      operationId: listContainers
      tags: [Discovery]
      summary: List discovered Docker containers
      responses:
        '200':
          description: List of discovered containers
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/DiscoveredContainer'
```

Add to the `schemas:` section:

```yaml
    DiscoveredContainer:
      type: object
      required: [name, image, status, labels]
      properties:
        name:
          type: string
        image:
          type: string
        status:
          type: string
        labels:
          type: object
          additionalProperties:
            type: string
        assignedDomain:
          type: string
          nullable: true
        conflictReason:
          type: string
          nullable: true
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk/Controllers/DiscoveryController.cs src/Schleusenwerk/Api/ApiModels.cs src/Schleusenwerk.Contracts/openapi.yaml
git commit -m "feat(api): add Docker discovery REST endpoint"
```

---

## Task 3: Hafenbecken UI + Discovery Store

**Files:**
- Create: `src/Schleusenwerk.Web/src/stores/discovery.ts`
- Modify: `src/Schleusenwerk.Web/src/api/client.ts`
- Modify: `src/Schleusenwerk.Web/src/pages/Hafenbecken.vue`

- [ ] **Step 1: Add discovery types and API method to client.ts**

Add the interface after the existing interfaces in `src/Schleusenwerk.Web/src/api/client.ts`:

```typescript
export interface DiscoveredContainer {
  name: string
  image: string
  status: string
  labels: Record<string, string>
  assignedDomain: string | null
  conflictReason: string | null
}
```

Add to the `api` export object, after the `health` section:

```typescript
  discovery: {
    listContainers: () => request<DiscoveredContainer[]>('/discovery/containers'),
  },
```

- [ ] **Step 2: Create discovery Pinia store**

Create `src/Schleusenwerk.Web/src/stores/discovery.ts`:

```typescript
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, type DiscoveredContainer } from '@/api/client'

export const useDiscoveryStore = defineStore('discovery', () => {
  const containers = ref<DiscoveredContainer[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const connected = ref(true)

  async function fetchContainers() {
    loading.value = true
    error.value = null
    try {
      containers.value = await api.discovery.listContainers()
      connected.value = true
    } catch (e: any) {
      error.value = e.message
      connected.value = false
    } finally {
      loading.value = false
    }
  }

  return { containers, loading, error, connected, fetchContainers }
})
```

- [ ] **Step 3: Replace Hafenbecken placeholder with real UI**

Replace the full content of `src/Schleusenwerk.Web/src/pages/Hafenbecken.vue`:

```vue
<template>
  <div>
    <div class="page-header">
      <h1>HAFENBECKEN</h1>
      <span class="separator">——</span>
      <span class="subtitle">Docker-Erkennung</span>
      <div style="margin-left: auto; display: flex; align-items: center; gap: 6px;">
        <div :class="discovery.connected ? 'bp-live-dot' : 'bp-status-dot gesperrt'" style="width: 7px; height: 7px;"></div>
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 11px;">
          {{ discovery.connected ? 'Socket verbunden' : 'Nicht verbunden' }}
        </span>
      </div>
    </div>

    <div v-if="discovery.loading" style="color: var(--bp-text-secondary);">Laden...</div>
    <div v-else-if="discovery.error" style="color: var(--bp-error);">{{ discovery.error }}</div>
    <template v-else>
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px;">
        <div v-for="container in discovery.containers" :key="container.name"
             class="bp-panel" :style="getContainerStyle(container)">
          <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 10px;">
            <StatusIndicator :status="getContainerStatus(container)" />
            <span style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); font-size: 13px; font-weight: 600;">
              {{ container.name }}
            </span>
            <span :style="{ color: getStatusColor(container), fontFamily: 'var(--bp-font-mono)', fontSize: '10px', marginLeft: 'auto' }">
              {{ getStatusLabel(container) }}
            </span>
          </div>

          <div style="font-family: var(--bp-font-mono); font-size: 11px; color: var(--bp-text-secondary); line-height: 1.8;">
            <div>Image: <span style="color: var(--bp-text-primary);">{{ container.image }}</span></div>
            <template v-if="hasLabels(container)">
              <div>Labels:</div>
              <div v-for="(value, key) in schleusenLabels(container)" :key="key" style="margin-left: 12px;">
                <span style="color: var(--bp-primary);">{{ key }}</span>=<span style="color: var(--bp-text-primary);">{{ value }}</span>
              </div>
            </template>
            <div v-else style="color: var(--bp-border);">Keine Schleusenwerk-Labels erkannt</div>
          </div>

          <div v-if="container.assignedDomain" style="margin-top: 10px; display: flex; align-items: center; gap: 6px; font-size: 11px;">
            <span style="color: var(--bp-primary);">→</span>
            <span style="color: var(--bp-text-secondary);">Zugeordnet an Tor</span>
            <RouterLink :to="`/tore/${container.assignedDomain}`"
                        style="color: var(--bp-text-primary); font-family: var(--bp-font-mono); text-decoration: underline; text-decoration-color: var(--bp-border);">
              {{ container.assignedDomain }}
            </RouterLink>
          </div>

          <div v-if="container.conflictReason" style="margin-top: 10px; padding: 6px 10px; background: rgba(240,192,0,0.06); border: 1px solid rgba(240,192,0,0.15); border-radius: 4px;">
            <span style="color: var(--bp-warning); font-size: 11px;">{{ container.conflictReason }}</span>
          </div>
        </div>
      </div>

      <div v-if="discovery.containers.length === 0" class="bp-panel" style="text-align: center;">
        <span style="color: var(--bp-text-secondary); font-family: var(--bp-font-mono); font-size: 12px;">
          Keine Container erkannt
        </span>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import StatusIndicator from '@/components/StatusIndicator.vue'
import { useDiscoveryStore } from '@/stores/discovery'
import type { DiscoveredContainer } from '@/api/client'

const discovery = useDiscoveryStore()

onMounted(() => discovery.fetchContainers())

function hasLabels(c: DiscoveredContainer): boolean {
  return Object.keys(c.labels).some(k => k.startsWith('schleusenwerk.'))
}

function schleusenLabels(c: DiscoveredContainer): Record<string, string> {
  return Object.fromEntries(Object.entries(c.labels).filter(([k]) => k.startsWith('schleusenwerk.')))
}

function getContainerStatus(c: DiscoveredContainer): 'offen' | 'gesperrt' | 'warnung' | 'neutral' {
  if (c.conflictReason) { return 'warnung' }
  if (!hasLabels(c)) { return 'neutral' }
  return 'offen'
}

function getContainerStyle(c: DiscoveredContainer): string {
  if (c.conflictReason) { return 'border-color: rgba(240,192,0,0.3);' }
  if (!hasLabels(c)) { return 'opacity: 0.6;' }
  return ''
}

function getStatusColor(c: DiscoveredContainer): string {
  if (c.conflictReason) { return 'var(--bp-warning)' }
  if (!hasLabels(c)) { return 'var(--bp-text-secondary)' }
  return 'var(--bp-primary)'
}

function getStatusLabel(c: DiscoveredContainer): string {
  if (c.conflictReason) { return 'Konflikt' }
  if (!hasLabels(c)) { return 'kein Label' }
  return c.status
}
</script>
```

- [ ] **Step 4: Verify Vue build**

Run from `src/Schleusenwerk.Web/`:
```bash
npx vue-tsc --noEmit && npx vite build
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk.Web/src/stores/discovery.ts src/Schleusenwerk.Web/src/api/client.ts src/Schleusenwerk.Web/src/pages/Hafenbecken.vue
git commit -m "feat(web): implement Hafenbecken page with Docker discovery API"
```

---

## Task 4: Custom Certificate Upload Endpoint

**Files:**
- Modify: `src/Schleusenwerk/Controllers/CertificateController.cs`
- Modify: `src/Schleusenwerk.Contracts/openapi.yaml`

- [ ] **Step 1: Add upload endpoint to CertificateController**

Add to `src/Schleusenwerk/Controllers/CertificateController.cs`, after the `ProvisionCertificate` method:

```csharp
    [HttpPost("{domain}/upload")]
    [Consumes("multipart/form-data")]
    public ActionResult<CommandResultDto> UploadCertificate(
        string domain,
        IFormFile file,
        IFormFile? keyFile = null,
        [FromForm] string? password = null)
    {
        var domainName = DomainName.Parse(domain);

        try
        {
            using var cert = LoadCertificate(file, keyFile, password);

            if (cert.NotAfter < DateTime.UtcNow)
            {
                return Ok(CommandResultDto.Fail("Certificate is expired"));
            }

            if (!CertificateMatchesDomain(cert, domain))
            {
                return Ok(CommandResultDto.Fail($"Certificate does not match domain '{domain}'"));
            }

            if (!cert.HasPrivateKey)
            {
                return Ok(CommandResultDto.Fail("Certificate has no private key"));
            }

            _store.StoreCertificate(domainName, cert);
            return Ok(CommandResultDto.Ok());
        }
        catch (Exception ex)
        {
            return Ok(CommandResultDto.Fail($"Failed to load certificate: {ex.Message}"));
        }
    }

    private static X509Certificate2 LoadCertificate(IFormFile file, IFormFile? keyFile, string? password)
    {
        using var stream = new MemoryStream();
        file.CopyTo(stream);
        var bytes = stream.ToArray();

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is ".pfx" or ".p12")
        {
            return new X509Certificate2(bytes, password, X509KeyStorageFlags.Exportable);
        }

        var certPem = System.Text.Encoding.UTF8.GetString(bytes);

        if (keyFile is null)
        {
            throw new InvalidOperationException("PEM certificate requires a separate key file");
        }

        using var keyStream = new MemoryStream();
        keyFile.CopyTo(keyStream);
        var keyPem = System.Text.Encoding.UTF8.GetString(keyStream.ToArray());

        return X509Certificate2.CreateFromPem(certPem, keyPem);
    }

    private static bool CertificateMatchesDomain(X509Certificate2 cert, string domain)
    {
        var san = cert.Extensions["2.5.29.17"];
        if (san is not null)
        {
            var sanString = san.Format(false);
            if (sanString.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        return string.Equals(cn, domain, StringComparison.OrdinalIgnoreCase);
    }
```

Also add the required using at the top of the file:

```csharp
using System.Security.Cryptography.X509Certificates;
```

- [ ] **Step 2: Add upload endpoint to openapi.yaml**

Add to the certificates section in `src/Schleusenwerk.Contracts/openapi.yaml`, after the `/api/certificates/{domain}/provision` path:

```yaml
  /api/certificates/{domain}/upload:
    parameters:
      - name: domain
        in: path
        required: true
        schema:
          type: string
    post:
      operationId: uploadCertificate
      tags: [Certificates]
      summary: Upload a custom certificate (PEM or PFX)
      requestBody:
        required: true
        content:
          multipart/form-data:
            schema:
              type: object
              required: [file]
              properties:
                file:
                  type: string
                  format: binary
                  description: PFX file or PEM cert file
                keyFile:
                  type: string
                  format: binary
                  description: PEM key file (required for PEM, not used for PFX)
                password:
                  type: string
                  description: PFX password (optional)
      responses:
        '200':
          description: Command result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommandResult'
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/Controllers/CertificateController.cs src/Schleusenwerk.Contracts/openapi.yaml
git commit -m "feat(api): add custom certificate upload endpoint (PEM + PFX)"
```

---

## Task 5: Siegel UI — Upload Button

**Files:**
- Modify: `src/Schleusenwerk.Web/src/api/client.ts`
- Modify: `src/Schleusenwerk.Web/src/stores/certificates.ts`
- Modify: `src/Schleusenwerk.Web/src/pages/Siegel.vue`

- [ ] **Step 1: Add upload API method to client.ts**

Add to the `certificates` section in the `api` object in `src/Schleusenwerk.Web/src/api/client.ts`:

```typescript
    upload: (domain: string, file: File, keyFile?: File, password?: string) => {
      const form = new FormData()
      form.append('file', file)
      if (keyFile) { form.append('keyFile', keyFile) }
      if (password) { form.append('password', password) }
      return fetch(`${BASE}/certificates/${encodeURIComponent(domain)}/upload`, {
        method: 'POST',
        body: form,
      }).then(r => r.json() as Promise<CommandResult>)
    },
```

- [ ] **Step 2: Add upload action to certificates store**

Add to the return object in `src/Schleusenwerk.Web/src/stores/certificates.ts`:

```typescript
  async function upload(domain: string, file: File, keyFile?: File, password?: string): Promise<CommandResult> {
    return api.certificates.upload(domain, file, keyFile, password)
  }

  return { list, loading, fetchList, provision, upload }
```

- [ ] **Step 3: Add upload UI to Siegel.vue**

In `src/Schleusenwerk.Web/src/pages/Siegel.vue`, add a file upload row after the renew button column in the table. Replace the last `<th>` and last `<td>` to add an upload column:

Replace the header row:
```vue
<thead><tr><th style="width: 28px;"></th><th>Domain</th><th>Fingerabdruck</th><th>Gültig bis</th><th>Typ</th><th style="width: 80px;"></th></tr></thead>
```

With:
```vue
<thead><tr><th style="width: 28px;"></th><th>Domain</th><th>Fingerabdruck</th><th>Gültig bis</th><th>Typ</th><th style="width: 80px;"></th><th style="width: 80px;"></th></tr></thead>
```

After the Erneuern button `<td>`, add:

```vue
          <td>
            <label class="bp-btn-outline" style="font-size: 11px; padding: 2px 8px; cursor: pointer;">
              Upload
              <input type="file" style="display: none;" @change="(e) => handleUpload(cert.domain, e)" accept=".pem,.crt,.pfx,.p12" />
            </label>
          </td>
```

Add the handler function in the `<script setup>` section:

```typescript
async function handleUpload(domain: string, event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files?.length) { return }
  const file = input.files[0]
  const result = await certs.upload(domain, file)
  if (result.success) { await certs.fetchList() }
  input.value = ''
}
```

- [ ] **Step 4: Verify Vue build**

Run from `src/Schleusenwerk.Web/`:
```bash
npx vue-tsc --noEmit && npx vite build
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Schleusenwerk.Web/src/api/client.ts src/Schleusenwerk.Web/src/stores/certificates.ts src/Schleusenwerk.Web/src/pages/Siegel.vue
git commit -m "feat(web): add certificate upload button to Siegel page"
```

---

## Task 6: Production Docker Compose

**Files:**
- Create: `docker-compose.prod.yml`

- [ ] **Step 1: Create docker-compose.prod.yml**

Create `docker-compose.prod.yml` at the repository root:

```yaml
version: '3.8'

services:
  proxy:
    build:
      context: .
      dockerfile: src/Schleusenwerk/Dockerfile
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - db:/data
      - certs:/certs
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    networks:
      - schleusenwerk

  web:
    build:
      context: src/Schleusenwerk.Web
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    networks:
      - schleusenwerk
    depends_on:
      - proxy

volumes:
  db:
  certs:

networks:
  schleusenwerk:
    driver: bridge
```

- [ ] **Step 2: Commit**

```bash
git add docker-compose.prod.yml
git commit -m "feat(deploy): add production docker-compose without exposed management port"
```

---

## Task 7: ACME Infrastructure — IAcmeClient + ChallengeStore

**Files:**
- Create: `src/Schleusenwerk/Certificates/IAcmeClient.cs`
- Create: `src/Schleusenwerk/Certificates/CertesAcmeClient.cs`
- Create: `src/Schleusenwerk/Certificates/AcmeChallengeStore.cs`
- Modify: `src/Schleusenwerk.Core/Persistence/ProxySettings.cs`
- Modify: `src/Directory.Packages.props`
- Modify: `src/Schleusenwerk/Schleusenwerk.csproj`

- [ ] **Step 1: Add Certes package**

Add to `src/Directory.Packages.props` in a new ItemGroup after the existing ones:

```xml
    <ItemGroup Label="ACME">
        <PackageVersion Include="Certes" Version="3.0.4"/>
    </ItemGroup>
```

Add to `src/Schleusenwerk/Schleusenwerk.csproj` in the PackageReference ItemGroup:

```xml
        <PackageReference Include="Certes" />
```

- [ ] **Step 2: Add AcmeEmail to ProxySettings**

In `src/Schleusenwerk.Core/Persistence/ProxySettings.cs`, add after `AcmeStage Stage`:

```csharp
    public string AcmeEmail { get; init; } = "";
```

- [ ] **Step 3: Create AcmeChallengeStore**

Create `src/Schleusenwerk/Certificates/AcmeChallengeStore.cs`:

```csharp
using System.Collections.Concurrent;

namespace Schleusenwerk.Certificates;

public sealed class AcmeChallengeStore
{
    private readonly ConcurrentDictionary<string, string> _challenges = new();

    public void SetChallenge(string token, string keyAuthorization)
    {
        _challenges[token] = keyAuthorization;
    }

    public string? GetChallenge(string token)
    {
        return _challenges.TryGetValue(token, out var value) ? value : null;
    }

    public void RemoveChallenge(string token)
    {
        _challenges.TryRemove(token, out _);
    }
}
```

- [ ] **Step 4: Create IAcmeClient interface**

Create `src/Schleusenwerk/Certificates/IAcmeClient.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

public sealed record AcmeOrderResult(string Token, string KeyAuthorization);

public interface IAcmeClient
{
    Task<AcmeOrderResult> StartOrderAsync(DomainName domain, CancellationToken ct = default);
    Task<X509Certificate2> CompleteOrderAsync(DomainName domain, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create CertesAcmeClient**

Create `src/Schleusenwerk/Certificates/CertesAcmeClient.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Certificates;

internal sealed class CertesAcmeClient : IAcmeClient
{
    private readonly IConfigurationStore _configStore;
    private readonly string _accountKeyPath;
    private readonly ILogger<CertesAcmeClient> _logger;

    private AcmeContext? _acme;
    private IOrderContext? _currentOrder;

    public CertesAcmeClient(
        IConfigurationStore configStore,
        string certsPath,
        ILogger<CertesAcmeClient> logger)
    {
        _configStore = configStore;
        _accountKeyPath = Path.Combine(certsPath, "acme-account.pem");
        _logger = logger;
    }

    public async Task<AcmeOrderResult> StartOrderAsync(DomainName domain, CancellationToken ct)
    {
        await EnsureAccountAsync(ct);

        _currentOrder = await _acme!.NewOrder(new[] { domain.Value });
        var authz = (await _currentOrder.Authorizations()).First();
        var challenge = await authz.Http();

        return new AcmeOrderResult(challenge.Token, challenge.KeyAuthz);
    }

    public async Task<X509Certificate2> CompleteOrderAsync(DomainName domain, CancellationToken ct)
    {
        if (_currentOrder is null)
        {
            throw new InvalidOperationException("No pending order. Call StartOrderAsync first.");
        }

        var authz = (await _currentOrder.Authorizations()).First();
        var challenge = await authz.Http();

        var challengeResult = await challenge.Validate();
        var maxWait = TimeSpan.FromSeconds(120);
        var waited = TimeSpan.Zero;
        var pollInterval = TimeSpan.FromSeconds(2);

        while (challengeResult.Status == Certes.Acme.Resource.ChallengeStatus.Pending ||
               challengeResult.Status == Certes.Acme.Resource.ChallengeStatus.Processing)
        {
            if (waited >= maxWait)
            {
                throw new TimeoutException($"ACME challenge validation timed out after {maxWait.TotalSeconds}s");
            }

            await Task.Delay(pollInterval, ct);
            waited += pollInterval;
            challengeResult = await challenge.Resource();
        }

        if (challengeResult.Status != Certes.Acme.Resource.ChallengeStatus.Valid)
        {
            throw new InvalidOperationException(
                $"ACME challenge failed with status {challengeResult.Status}: {challengeResult.Error?.Detail}");
        }

        var privateKey = KeyFactory.NewKey(Certes.KeyAlgorithm.RS256);
        var certChain = await _currentOrder.Generate(
            new CsrInfo { CommonName = domain.Value }, privateKey);

        var pfxBytes = certChain.ToPfx(privateKey).Build(domain.Value, "");
        _currentOrder = null;

        return new X509Certificate2(pfxBytes, "", X509KeyStorageFlags.Exportable);
    }

    private async Task EnsureAccountAsync(CancellationToken ct)
    {
        if (_acme is not null)
        {
            return;
        }

        var settings = await _configStore.GetSettingsAsync(ct);
        var acmeUri = settings.Stage switch
        {
            AcmeStage.Staging => WellKnownServers.LetsEncryptStagingV2,
            AcmeStage.Production => WellKnownServers.LetsEncryptV2,
            _ => throw new InvalidOperationException($"ACME not available for stage {settings.Stage}")
        };

        if (File.Exists(_accountKeyPath))
        {
            var pem = await File.ReadAllTextAsync(_accountKeyPath, ct);
            var accountKey = KeyFactory.FromPem(pem);
            _acme = new AcmeContext(acmeUri, accountKey);
            _logger.LogInformation("Loaded existing ACME account from {Path}", _accountKeyPath);
        }
        else
        {
            _acme = new AcmeContext(acmeUri);
            var email = string.IsNullOrWhiteSpace(settings.AcmeEmail) ? null : settings.AcmeEmail;
            await _acme.NewAccount(email, termsOfServiceAgreed: true);

            var keyPem = _acme.AccountKey.ToPem();
            var dir = Path.GetDirectoryName(_accountKeyPath)!;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_accountKeyPath, keyPem, ct);
            _logger.LogInformation("Created new ACME account, key saved to {Path}", _accountKeyPath);
        }
    }
}
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Schleusenwerk/Certificates/IAcmeClient.cs src/Schleusenwerk/Certificates/CertesAcmeClient.cs src/Schleusenwerk/Certificates/AcmeChallengeStore.cs src/Schleusenwerk.Core/Persistence/ProxySettings.cs src/Directory.Packages.props src/Schleusenwerk/Schleusenwerk.csproj
git commit -m "feat(acme): add Certes ACME client, challenge store, and IAcmeClient interface"
```

---

## Task 8: ACME Challenge Endpoint + DI Registration

**Files:**
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`
- Modify: `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`

- [ ] **Step 1: Register ACME services in SchleusenwerkServicesSetup**

In `src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs`, add after the `services.AddSingleton<SniCertificateSelector>();` line:

```csharp
        services.AddSingleton<AcmeChallengeStore>();
        services.AddSingleton<IAcmeClient>(sp =>
            new CertesAcmeClient(
                sp.GetRequiredService<IConfigurationStore>(),
                certsPath,
                sp.GetRequiredService<ILogger<CertesAcmeClient>>()));
```

Also add the required using:

```csharp
using Schleusenwerk.Certificates;
```

- [ ] **Step 2: Add ACME challenge endpoint in SchleusenwerkApplicationSetup**

In `src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs`, add after the `app.MapGet("/health", ...)` line:

```csharp
        app.MapGet("/.well-known/acme-challenge/{token}", (string token, AcmeChallengeStore store) =>
        {
            var keyAuthz = store.GetChallenge(token);
            return keyAuthz is not null ? Results.Text(keyAuthz) : Results.NotFound();
        });
```

Also add the required using:

```csharp
using Schleusenwerk.Certificates;
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Schleusenwerk/Startup/SchleusenwerkServicesSetup.cs src/Schleusenwerk/Startup/SchleusenwerkApplicationSetup.cs
git commit -m "feat(acme): register ACME services and add HTTP-01 challenge endpoint"
```

---

## Task 9: Extend CertificateProvisioningActor with ACME + Auto-Renewal

**Files:**
- Modify: `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs`

- [ ] **Step 1: Rewrite CertificateProvisioningActor with ACME support**

Replace the full content of `src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs`:

```csharp
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Schleusenwerk.Persistence;
using Schleusenwerk.Routing;
using Servus.Akka;

namespace Schleusenwerk.Certificates;

public sealed class CertificateProvisioningActor : ReceiveActor, IWithTimers
{
    private static readonly TimeSpan RenewalCheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan RenewalThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan WarningThreshold = TimeSpan.FromDays(14);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ICertificateStore _store;
    private readonly IConfigurationStore _configStore;
    private readonly IAcmeClient _acmeClient;
    private readonly AcmeChallengeStore _challengeStore;
    private readonly IActorRef _eventHub;

    public CertificateProvisioningActor(
        ICertificateStore store,
        IConfigurationStore configStore,
        IAcmeClient acmeClient,
        AcmeChallengeStore challengeStore)
    {
        _store = store;
        _configStore = configStore;
        _acmeClient = acmeClient;
        _challengeStore = challengeStore;
        _eventHub = Context.GetActor<EventHub>();

        Receive<CertificateProvisioningRequested>(Handle);
        Receive<CheckRenewals>(_ => HandleCheckRenewals());
        Receive<ProvisioningResult>(Handle);
        Receive<EventHub.Subscribed>(msg =>
        {
            msg.SourceRef.Source
                .RunWith(
                    Sink.ActorRef<IClusterEvent>(Self, StreamCompleted.Instance, ex => new StreamFailed(ex)),
                    Context.Materializer());
        });
        Receive<StreamCompleted>(_ =>
            _log.Warning("Certificate event stream completed unexpectedly"));
        Receive<StreamFailed>(msg =>
            _log.Error(msg.Ex, "Certificate event stream failed"));
    }

    protected override void PreStart()
    {
        base.PreStart();
        _eventHub.Ask<EventHub.Subscribed>(EventHub.Subscribe<ICertificateEvent>.Instance)
            .PipeTo(Self);

        Timers.StartPeriodicTimer("renewal-check", CheckRenewals.Instance, RenewalCheckInterval, RenewalCheckInterval);
    }

    private void Handle(CertificateProvisioningRequested msg)
    {
        ProvisionAsync(msg.DomainName, 0);
    }

    private void ProvisionAsync(DomainName domain, int attempt)
    {
        var self = Self;

        Task.Run(async () =>
        {
            try
            {
                var settings = await _configStore.GetSettingsAsync();

                if (settings.Stage == AcmeStage.Local)
                {
                    if (_store.HasCertificate(domain))
                    {
                        return new ProvisioningResult(domain, true, null, attempt);
                    }

                    using var cert = SelfSignedCertificateGenerator.Generate(domain);
                    _store.StoreCertificate(domain, cert);
                    return new ProvisioningResult(domain, true, null, attempt);
                }

                var order = await _acmeClient.StartOrderAsync(domain);
                _challengeStore.SetChallenge(order.Token, order.KeyAuthorization);

                try
                {
                    using var cert = await _acmeClient.CompleteOrderAsync(domain);
                    _store.StoreCertificate(domain, cert);
                    return new ProvisioningResult(domain, true, null, attempt);
                }
                finally
                {
                    _challengeStore.RemoveChallenge(order.Token);
                }
            }
            catch (Exception ex)
            {
                return new ProvisioningResult(domain, false, ex.Message, attempt);
            }
        }).PipeTo(self);
    }

    private void Handle(ProvisioningResult msg)
    {
        if (msg.Success)
        {
            _log.Info("Certificate provisioned for {Domain}", msg.Domain);
        }
        else
        {
            _log.Warning("Certificate provisioning failed for {Domain}: {Error}", msg.Domain, msg.Error);

            var delay = TimeSpan.FromMinutes(Math.Min(Math.Pow(2, msg.Attempt), MaxRetryDelay.TotalMinutes));
            _log.Info("Retrying provisioning for {Domain} in {Delay}", msg.Domain, delay);
            Timers.StartSingleTimer(
                $"retry-{msg.Domain.Value}",
                new CertificateProvisioningRequested(msg.Domain),
                delay);
        }
    }

    private void HandleCheckRenewals()
    {
        foreach (var domain in _store.ListDomains())
        {
            var cert = _store.GetCertificate(domain);
            if (cert is null)
            {
                continue;
            }

            var remaining = cert.NotAfter - DateTime.UtcNow;

            if (remaining < WarningThreshold)
            {
                _eventHub.Tell(new CertificateExpiring(domain));
            }

            if (remaining < RenewalThreshold)
            {
                _log.Info("Certificate for {Domain} expires in {Days} days, triggering renewal", domain, (int)remaining.TotalDays);
                Self.Tell(new CertificateProvisioningRequested(domain));
            }
        }
    }

    private sealed record CheckRenewals
    {
        public static readonly CheckRenewals Instance = new();
    }

    private sealed record ProvisioningResult(DomainName Domain, bool Success, string? Error, int Attempt);

    private sealed record StreamCompleted
    {
        public static readonly StreamCompleted Instance = new();
    }

    private sealed record StreamFailed(Exception Ex);
}
```

- [ ] **Step 2: Add CertificateExpiring event to Schleusenwerk.Core**

Check if `CertificateExpiring` already exists. If not, add to `src/Schleusenwerk.Core/Persistence/ConfigurationEvents.cs`:

```csharp
public sealed record CertificateExpiring(DomainName DomainName) : ICertificateEvent;
```

- [ ] **Step 3: Update DomainModelMapper to handle CertificateExpiring**

In `src/Schleusenwerk/Api/DomainModelMapper.cs`, update the `CanMapToProxyEvent` method:

```csharp
    public static bool CanMapToProxyEvent(IClusterEvent evt)
        => evt is DomainConfigured or DomainDeactivated or UpstreamHealthChanged
           or CertificateProvisioningRequested or CertificateExpiring;
```

Add a case to `ToProxyEvent`:

```csharp
        CertificateExpiring e => new ProxyEventDto("CertificateExpiring", e.DomainName.Value, "", false, ""),
```

- [ ] **Step 4: Update actor system setup to pass new dependencies**

In `src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs`, find the cert provisioning actor registration and update it to pass the new dependencies. Find:

```csharp
            var certProvisioning = system.ActorOf(resolver.Props<CertificateProvisioningActor>(), "cert-provisioning");
```

Replace with:

```csharp
            var certProvisioning = system.ActorOf(
                Props.Create(() => new CertificateProvisioningActor(
                    serviceProvider.GetRequiredService<ICertificateStore>(),
                    serviceProvider.GetRequiredService<IConfigurationStore>(),
                    serviceProvider.GetRequiredService<IAcmeClient>(),
                    serviceProvider.GetRequiredService<AcmeChallengeStore>())),
                "cert-provisioning");
```

Add required usings at the top:

```csharp
using Schleusenwerk.Certificates;
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: Build succeeds.

- [ ] **Step 6: Run tests**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Schleusenwerk/Certificates/CertificateProvisioningActor.cs src/Schleusenwerk.Core/Persistence/ConfigurationEvents.cs src/Schleusenwerk/Api/DomainModelMapper.cs src/Schleusenwerk/Startup/SchleusenwerkActorSystemSetup.cs
git commit -m "feat(acme): extend CertificateProvisioningActor with ACME/Let's Encrypt and auto-renewal"
```

---

## Task 10: Final Build Verification

**Files:**
- No new files

- [ ] **Step 1: Full .NET build**

Run: `dotnet build --configuration Release src/Schleusenwerk.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet run --project src/Schleusenwerk.Tests/Schleusenwerk.Tests.csproj`
Expected: All tests pass.

- [ ] **Step 3: Vue build**

Run from `src/Schleusenwerk.Web/`:
```bash
npx vue-tsc --noEmit && npx vite build
```

Expected: TypeScript clean, Vite build succeeds.
