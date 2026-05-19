---
name: toolengine-advance-phase6-cloud-native
description: >
  Makes ToolEngine v2026 production-deployable at enterprise scale on Kubernetes.
  Covers: Helm charts for all services with production-grade resource limits and
  pod disruption budgets, KEDA autoscaling driven by Service Bus queue depth,
  GitOps pipeline with ArgoCD for declarative deployment and drift detection,
  multi-region active-passive with PostgreSQL streaming replication and Redis
  Sentinel, chaos engineering hooks for resilience validation, and automated
  tenant onboarding via API and Terraform module.
classification: Confidential - Internal Use Only
---

# Advancement Phase 6 — Cloud-Native Operations & Scalability

## Prerequisites

Phase A1 (Security & Resilience) complete.
Kubernetes cluster available (AKS, EKS, or GKE).
Helm 3.x, kubectl, Terraform 1.5+ installed.
cert-manager installed in cluster for mTLS certificate management.

---

## Overview

| Item | Description | Tool |
|------|-------------|------|
| A6.1 | Helm charts for Kubernetes | Helm 3 |
| A6.2 | KEDA autoscaling | KEDA v2 |
| A6.3 | GitOps pipeline | ArgoCD / Flux |
| A6.4 | Multi-region active-passive | PostgreSQL + Redis Sentinel |
| A6.5 | Chaos engineering | Chaos Mesh |
| A6.6 | Automated tenant onboarding | API + Terraform |

---

## A6.1 — Helm Charts

### Why

There are no Kubernetes deployment artifacts today. Helm charts provide
parameterised, versionable deployment manifests — enabling repeatable
deployments across dev, staging, and prod environments with environment-specific
overrides.

### Chart structure — `deploy/helm/toolengine/`

```
toolengine/
  Chart.yaml
  values.yaml                 — default values (prod baseline)
  values.dev.yaml             — dev overrides
  values.staging.yaml         — staging overrides
  templates/
    api/
      deployment.yaml
      service.yaml
      ingress.yaml
      hpa.yaml
      pdb.yaml                — PodDisruptionBudget
    worker/
      deployment.yaml
      service.yaml
    frontend/
      deployment.yaml
      service.yaml
      ingress.yaml
    shared/
      serviceaccount.yaml
      configmap.yaml
      secret.yaml             — references to Kubernetes Secrets / Key Vault
      networkpolicy.yaml      — deny-by-default network policy
```

### `Chart.yaml`

```yaml
apiVersion: v2
name: toolengine
description: ONE BCG ToolEngine — Multi-tenant AI tool execution platform
type: application
version: 1.0.0
appVersion: "2026.1.0"

dependencies:
  - name: postgresql
    version: "14.*"
    repository: https://charts.bitnami.com/bitnami
    condition: postgresql.enabled
  - name: redis
    version: "18.*"
    repository: https://charts.bitnami.com/bitnami
    condition: redis.enabled
```

### `values.yaml` — production baseline

```yaml
api:
  replicaCount: 3
  image:
    repository: ghcr.io/onebcg/toolengine-api
    tag: "2026.1.0"
    pullPolicy: IfNotPresent
  resources:
    requests: { cpu: 250m, memory: 512Mi }
    limits:   { cpu: 1000m, memory: 1Gi }
  autoscaling:
    enabled:     true
    minReplicas: 3
    maxReplicas: 20
    targetCPUUtilizationPercentage: 70
  podDisruptionBudget:
    minAvailable: 2          # always keep 2 pods running during rolling updates
  affinity:
    podAntiAffinity:
      requiredDuringSchedulingIgnoredDuringExecution:
        - topologyKey: kubernetes.io/hostname  # spread across nodes
  livenessProbe:
    httpGet: { path: /healthz/live, port: 8080 }
    initialDelaySeconds: 15
    periodSeconds: 10
  readinessProbe:
    httpGet: { path: /healthz/ready, port: 8080 }
    initialDelaySeconds: 5
    periodSeconds: 5

worker:
  replicaCount: 2
  image:
    repository: ghcr.io/onebcg/toolengine-worker
    tag: "2026.1.0"
  resources:
    requests: { cpu: 100m, memory: 256Mi }
    limits:   { cpu: 500m, memory: 512Mi }

ingress:
  enabled:   true
  className: nginx
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
  hosts:
    - host: api.toolengine.onebcg.com
      paths: [{ path: /, pathType: Prefix }]
  tls:
    - secretName: toolengine-api-tls
      hosts: [api.toolengine.onebcg.com]
```

### `api/deployment.yaml` — key sections

```yaml
spec:
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge:       1
      maxUnavailable: 0    # zero-downtime deployments
  template:
    spec:
      serviceAccountName: toolengine-api
      securityContext:
        runAsNonRoot: true
        runAsUser:    1000
        fsGroup:      2000
        seccompProfile:
          type: RuntimeDefault
      containers:
        - name: api
          image: "{{ .Values.api.image.repository }}:{{ .Values.api.image.tag }}"
          ports: [{ containerPort: 8080, name: http }]
          env:
            - name: Database__Provider
              value: postgresql
            - name: Database__ConnectionString
              valueFrom:
                secretKeyRef:
                  name: toolengine-secrets
                  key:  db-connection-string
          readinessProbe:
            {{- toYaml .Values.api.readinessProbe | nindent 12 }}
          resources:
            {{- toYaml .Values.api.resources | nindent 12 }}
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem:   true
            capabilities:
              drop: [ALL]
```

### Network policy — `shared/networkpolicy.yaml`

```yaml
# Default deny all ingress and egress
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: toolengine-default-deny
spec:
  podSelector: {}
  policyTypes: [Ingress, Egress]
---
# Allow API → worker (internal service bus)
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: toolengine-api-to-worker
spec:
  podSelector:
    matchLabels: { app: toolengine-api }
  egress:
    - to:
        - podSelector:
            matchLabels: { app: toolengine-worker }
      ports: [{ port: 8081 }]
```

---

## A6.2 — KEDA Autoscaling

### Why

The approval notification worker processes bursts of outbox messages — heavy
during business hours, idle overnight. HPA scales on CPU which lags queue depth.
KEDA scales directly on Service Bus queue length, giving sub-minute scale-out
when the approval backlog grows.

### KEDA installation

```bash
helm repo add kedacore https://kedacore.github.io/charts
helm install keda kedacore/keda --namespace keda --create-namespace
```

### `ScaledObject` for notification worker

```yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: toolengine-worker-scaler
  namespace: toolengine
spec:
  scaleTargetRef:
    name: toolengine-worker
  minReplicaCount: 1
  maxReplicaCount: 20
  cooldownPeriod:  60     # seconds to wait before scaling down
  triggers:
    - type: azure-servicebus
      metadata:
        queueName:              toolengine-approvals
        namespace:              toolengine-sb.servicebus.windows.net
        messageCount:           "5"     # scale up when >5 messages queued
        activationMessageCount: "1"
      authenticationRef:
        name: keda-sb-auth

    # Also scale on custom OTel metric — pending approval count
    - type: metrics-api
      metadata:
        url:       http://prometheus:9090/api/v1/query
        query:     "tool_approval_pending_count{namespace='toolengine'}"
        threshold: "10"
```

### KEDA trigger authentication

```yaml
apiVersion: keda.sh/v1alpha1
kind: TriggerAuthentication
metadata:
  name: keda-sb-auth
  namespace: toolengine
spec:
  podIdentity:
    provider: azure-workload  # Azure Workload Identity — no secrets in YAML
```

---

## A6.3 — GitOps Pipeline (ArgoCD)

### Why

Manual `helm upgrade` commands are unaudited, unrepeatable, and error-prone.
ArgoCD continuously reconciles the running cluster state with the git repository.
Any drift (manual kubectl change, config mutation) is detected and reverted.

### ArgoCD Application — `deploy/argocd/toolengine-production.yaml`

```yaml
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: toolengine-production
  namespace: argocd
  finalizers: [resources-finalizer.argocd.argoproj.io]
spec:
  project: default
  source:
    repoURL:        https://github.com/onebcg/toolengine
    targetRevision: main
    path:           deploy/helm/toolengine
    helm:
      valueFiles:
        - values.yaml
        - values.prod.yaml
      parameters:
        - name: api.image.tag
          value: "{{ .Values.imageTag }}"    # set by CI pipeline
  destination:
    server: https://kubernetes.default.svc
    namespace: toolengine
  syncPolicy:
    automated:
      prune:    true     # remove resources deleted from git
      selfHeal: true     # revert manual cluster changes
    syncOptions:
      - CreateNamespace=true
      - PrunePropagationPolicy=foreground
      - RespectIgnoreDifferences=true
    retry:
      limit: 5
      backoff:
        duration:    5s
        factor:      2
        maxDuration: 3m
```

### CI → GitOps flow — `.github/workflows/deploy.yml`

```yaml
- name: Update image tag in values
  run: |
    VERSION=${GITHUB_SHA::8}
    # Update the production values file with the new image tag
    yq eval ".api.image.tag = \"${VERSION}\"" \
      -i deploy/helm/toolengine/values.prod.yaml

    git config user.email "ci@onebcg.com"
    git config user.name  "CI Bot"
    git commit -am "ci: deploy api image ${VERSION} to production"
    git push origin main
    # ArgoCD detects the commit and syncs automatically
```

---

## A6.4 — Multi-Region Active-Passive

### Why

Single-region failure takes down the entire platform. For AI workloads that
are embedded in client-facing workflows, this is a client-impacting incident.
Active-passive with automatic failover gives RTO < 5 minutes.

### PostgreSQL streaming replication — `deploy/terraform/modules/postgresql/`

```hcl
# Primary — Region A (e.g., East US)
resource "azurerm_postgresql_flexible_server" "primary" {
  name                = "toolengine-pg-primary"
  location            = var.primary_region
  sku_name            = "GP_Standard_D4s_v3"
  storage_mb          = 131072
  backup_retention_days = 35
  geo_redundant_backup  = "Enabled"   # cross-region backup
  high_availability {
    mode                      = "ZoneRedundant"
    standby_availability_zone = "2"
  }
}

# Read replica — Region B (e.g., West Europe)
# Promoted to primary during failover
resource "azurerm_postgresql_flexible_server" "replica" {
  name              = "toolengine-pg-replica"
  location          = var.secondary_region
  create_mode       = "Replica"
  source_server_id  = azurerm_postgresql_flexible_server.primary.id
}
```

### Redis Sentinel (high-availability cache)

```yaml
# In Helm values — enables Redis Sentinel mode (3 nodes, automatic failover)
redis:
  enabled:       true
  architecture:  replication
  auth:
    enabled:    true
    secretName: toolengine-redis-auth
  sentinel:
    enabled:            true
    masterSet:          toolengine-master
    quorum:             2       # majority needed to elect new master
    downAfterMilliseconds: 5000
    failoverTimeout:       10000
  replica:
    replicaCount: 2
    persistence:
      enabled: true
      size:    8Gi
```

### Global traffic routing — Azure Traffic Manager / AWS Route 53

```hcl
# Azure Traffic Manager — priority-based routing (active-passive)
resource "azurerm_traffic_manager_profile" "toolengine" {
  name                = "toolengine-global"
  traffic_routing_method = "Priority"

  dns_config {
    relative_name = "toolengine"
    ttl           = 30    # low TTL for fast failover
  }

  monitor_config {
    protocol    = "HTTPS"
    port        = 443
    path        = "/healthz/ready"
    interval    = 10
    timeout     = 5
    tolerated_number_of_failures = 3
  }
}

resource "azurerm_traffic_manager_azure_endpoint" "primary" {
  name               = "primary-eastus"
  profile_id         = azurerm_traffic_manager_profile.toolengine.id
  priority           = 1        # primary
  target_resource_id = azurerm_lb.primary.id
}

resource "azurerm_traffic_manager_azure_endpoint" "secondary" {
  name               = "secondary-westeurope"
  profile_id         = azurerm_traffic_manager_profile.toolengine.id
  priority           = 10       # failover
  target_resource_id = azurerm_lb.secondary.id
}
```

---

## A6.5 — Chaos Engineering

### Why

Polly v8 resilience pipelines (Phase A1.4) claim to handle LLM provider outages
and DB timeouts gracefully. This is only verified if tested. Chaos Mesh injects
real failures in a pre-production environment — circuit breakers, retries, and
fallbacks must be empirically confirmed.

### Chaos Mesh installation

```bash
helm repo add chaos-mesh https://charts.chaos-mesh.org
helm install chaos-mesh chaos-mesh/chaos-mesh \
  --namespace chaos-testing \
  --create-namespace
```

### Chaos experiment — LLM provider outage

```yaml
# Inject 100% packet loss to Anthropic API endpoint for 60 seconds
apiVersion: chaos-mesh.org/v1alpha1
kind: NetworkChaos
metadata:
  name: llm-provider-outage
  namespace: toolengine
spec:
  action:    loss
  mode:      all
  selector:
    namespaces: [toolengine]
    labelSelectors: { app: toolengine-api }
  loss:
    loss:        "100"
    correlation: "0"
  direction:     to
  externalTargets:
    - api.anthropic.com
  duration: 60s
```

### Chaos experiment — database connection timeout

```yaml
apiVersion: chaos-mesh.org/v1alpha1
kind: NetworkChaos
metadata:
  name: db-connection-delay
  namespace: toolengine
spec:
  action: delay
  mode:   all
  selector:
    namespaces: [toolengine]
  delay:
    latency:     "3000ms"   # 3 second delay — beyond Polly timeout threshold
    jitter:      "500ms"
    correlation: "50"
  direction: to
  target:
    selector:
      namespaces: [toolengine]
      labelSelectors: { app: postgresql }
  duration: 30s
```

### Chaos test runbook — `docs/chaos-runbook.md`

```
1. Verify baseline: all health checks green, SLO burn rate < 1.0
2. Apply NetworkChaos for LLM provider
3. Expected: circuit breaker opens within 30s, fallback provider serves requests
4. Expected: error rate < 1% (retry absorbs transient failures)
5. Remove chaos: circuit breaker half-opens, recovers within 60s
6. Apply NetworkChaos for DB (3s delay)
7. Expected: Polly timeout policy triggers, requests fail fast with 503
8. Expected: tool invocations return TIMEOUT error, not hang indefinitely
9. Verify: SLO error budget consumed < 0.1% (failures within tolerance)
10. Record results in chaos engineering log
```

### Schedule quarterly chaos test in CI

```yaml
# .github/workflows/chaos.yml
on:
  schedule:
    - cron: '0 2 1 */3 *'    # 2am on 1st of every 3rd month

jobs:
  chaos-test:
    runs-on: ubuntu-latest
    environment: staging
    steps:
      - name: Run chaos experiments
        run: |
          kubectl apply -f deploy/chaos/llm-provider-outage.yaml
          sleep 90
          kubectl delete -f deploy/chaos/llm-provider-outage.yaml
          # Assert: circuit breaker metric fired
          # Assert: fallback provider invoked
          # Assert: error rate < 1%
```

---

## A6.6 — Automated Tenant Onboarding

### Why

Tenant provisioning is manual today. Each new tenant requires: a database row,
namespace grants, budget configuration, initial tool set, and billing setup.
This takes 2–4 hours and is error-prone. Automation reduces it to 5 minutes.

### Onboarding API endpoint — `POST /admin/tenants`

```csharp
app.MapPost("/admin/tenants", async (
    [FromBody] CreateTenantRequest req,
    IMediator mediator,
    CancellationToken ct) =>
{
    var command = new CreateTenantCommand(
        TenantId:       req.Id,
        Name:           req.Name,
        AllowedNs:      req.AllowedNamespaces,
        DailyBudget:    req.DailyToolCallBudget,
        MaxTokens:      req.MaxResponseTokens,
        LlmProvider:    req.LlmProvider ?? "anthropic",
        AdminEmail:     req.AdminEmail);

    var result = await mediator.Send(command, ct);
    return result.IsSuccess
        ? Results.Created($"/admin/tenants/{req.Id}", result.Value)
        : Results.Problem(result.Error.Description);
})
.RequireAuthorization("Admin");
```

### Terraform tenant module — `terraform/modules/tenant/`

```hcl
# terraform/modules/tenant/main.tf
variable "tenant_id"            { type = string }
variable "allowed_namespaces"   { type = list(string) }
variable "daily_budget"         { type = number, default = 1000 }
variable "max_response_tokens"  { type = number, default = 4096 }
variable "admin_email"          { type = string }

# Call the onboarding API
resource "null_resource" "create_tenant" {
  triggers = { tenant_id = var.tenant_id }

  provisioner "local-exec" {
    command = <<-EOT
      curl -s -X POST ${var.api_url}/admin/tenants \
        -H "Authorization: Bearer ${var.admin_token}" \
        -H "Content-Type: application/json" \
        -d '${jsonencode({
          id                  = var.tenant_id,
          name                = var.tenant_id,
          allowedNamespaces   = var.allowed_namespaces,
          dailyToolCallBudget = var.daily_budget,
          maxResponseTokens   = var.max_response_tokens,
          adminEmail          = var.admin_email
        })}'
    EOT
  }
}

# Create Azure AD group for this tenant
resource "azuread_group" "tenant_group" {
  display_name     = "toolengine-${var.tenant_id}"
  security_enabled = true
}

# Store tenant credentials in Key Vault
resource "azurerm_key_vault_secret" "tenant_api_key" {
  name         = "toolengine-${var.tenant_id}-api-key"
  value        = random_password.api_key.result
  key_vault_id = data.azurerm_key_vault.toolengine.id
}

output "tenant_id"  { value = var.tenant_id }
output "api_key_kv" { value = azurerm_key_vault_secret.tenant_api_key.name }
```

### Tenant onboarding usage

```hcl
# In a client project's Terraform:
module "acme_corp_tenant" {
  source = "github.com/onebcg/toolengine//terraform/modules/tenant"

  tenant_id          = "acme-corp"
  allowed_namespaces = ["finance", "hr", "reporting"]
  daily_budget       = 5000
  max_response_tokens = 8192
  admin_email        = "admin@acme-corp.com"
  api_url            = "https://api.toolengine.onebcg.com"
  admin_token        = var.toolengine_admin_token
}

output "acme_api_key_secret" {
  value = module.acme_corp_tenant.api_key_kv
}
```

---

## Phase A6 Completion Checklist

### A6.1 — Helm Charts
- [ ] `Chart.yaml` with appVersion and dependencies (postgresql, redis)
- [ ] `values.yaml` has production-grade resource requests and limits
- [ ] `PodDisruptionBudget` with `minAvailable: 2` for API deployment
- [ ] Pod anti-affinity: spread across nodes (`required` scheduling rule)
- [ ] `RollingUpdate` strategy: `maxUnavailable: 0` (zero-downtime)
- [ ] Container `securityContext`: `runAsNonRoot`, `readOnlyRootFilesystem`, drop ALL capabilities
- [ ] `NetworkPolicy` default-deny with explicit API → worker allow rule
- [ ] All secrets referenced via `secretKeyRef`, never hardcoded in values

### A6.2 — KEDA
- [ ] KEDA installed in `keda` namespace
- [ ] `ScaledObject` targets notification worker, not API (API scales on CPU)
- [ ] Trigger: Service Bus queue length ≥ 5 messages triggers scale-up
- [ ] Auth via Azure Workload Identity (no secrets in YAML)
- [ ] `minReplicaCount: 1` (never scale to zero — approval dispatch must be responsive)
- [ ] `cooldownPeriod: 60` prevents thrashing

### A6.3 — GitOps
- [ ] `argocd/toolengine-production.yaml` with `automated.selfHeal: true`
- [ ] `prune: true` — removes resources deleted from git
- [ ] CI pipeline updates `values.prod.yaml` with new image tag
- [ ] ArgoCD sync webhook configured for sub-minute deploy detection
- [ ] Sync retry with exponential backoff configured

### A6.4 — Multi-Region
- [ ] PostgreSQL geo-redundant backup enabled
- [ ] Read replica in secondary region created via Terraform
- [ ] Redis Sentinel with 3 nodes and quorum = 2
- [ ] Traffic Manager / Route 53 with priority routing (10s health check interval)
- [ ] DNS TTL ≤ 30 seconds for fast failover
- [ ] Failover runbook documented and tested (RTO target: < 5 minutes)

### A6.5 — Chaos Engineering
- [ ] Chaos Mesh installed in `chaos-testing` namespace
- [ ] 2 chaos experiments defined: LLM provider outage, DB connection delay
- [ ] Chaos runbook documents expected vs. actual behaviour
- [ ] Quarterly chaos test scheduled in CI (`cron: '0 2 1 */3 *'`)
- [ ] Circuit breaker metric confirmed to fire during LLM outage test

### A6.6 — Tenant Onboarding
- [ ] `POST /admin/tenants` endpoint with `Admin` policy
- [ ] `CreateTenantCommand` seeds: DB row, namespace grants, budget, initial tools
- [ ] Terraform module in `terraform/modules/tenant/`
- [ ] Module creates: Azure AD group, Key Vault secret for API key
- [ ] End-to-end onboarding time < 5 minutes (manual step: zero)

---

*Confidential — Internal Use Only | ONE BCG | ToolEngine v2026*
