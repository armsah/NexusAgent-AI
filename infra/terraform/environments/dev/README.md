# NexusAgent AI — Development Infrastructure

## Overview

This directory defines the Azure development environment for NexusAgent AI
using Terraform.

The infrastructure is designed for a .NET-based AI agent platform with
API and background-worker workloads, messaging, document storage,
observability, and optional AI services.

**Status:** Infrastructure-as-Code validated. Azure deployment deferred.

## Technology Stack

- Terraform >= 1.9
- AzureRM provider 4.x
- Microsoft Azure (`swedencentral`)
- Azure Container Apps
- Azure Container Registry
- Azure PostgreSQL Flexible Server
- Azure Service Bus
- Azure Blob Storage
- Azure Key Vault
- Azure Monitor / Application Insights
- Azure AI Search (optional)
- Azure OpenAI (optional)

## Infrastructure Components

| Component                      | Purpose                                       |
| ------------------------------ | --------------------------------------------- |
| Resource Group                 | Logical resource organization                 |
| Container Registry             | Container image storage                       |
| Container Apps Environment     | Hosting environment for application workloads |
| API Container App              | HTTP API hosting                              |
| Worker Container App           | Background processing                         |
| PostgreSQL Flexible Server     | Relational data persistence                   |
| Service Bus                    | Asynchronous workflow messaging               |
| Blob Storage                   | Document storage                              |
| Key Vault                      | Secrets management                            |
| User-Assigned Managed Identity | Workload authentication                       |
| Log Analytics                  | Centralized logging                           |
| Application Insights           | Application observability                     |
| Azure AI Search                | Optional retrieval infrastructure             |
| Azure OpenAI                   | Optional AI model service                     |

## Deployment Controls

The following variables control optional infrastructure:

```hcl
deploy_api           = false
deploy_data_services = false
enable_ai_services   = false
```

When disabled:

- API and Worker Container Apps are excluded from the plan.
- PostgreSQL resources are excluded from the plan.
- Azure AI Search and Azure OpenAI resources are excluded from the plan.

Core infrastructure remains part of the Terraform plan.

**These flags do not make a deployment free.**

## Security

The configuration uses:

- User-assigned managed identity for application workloads.
- Azure RBAC for resource authorization.
- Container Registry authentication without an admin account.
- Service Bus with local authentication disabled.
- Storage with shared-key authentication disabled.
- Microsoft Entra ID authentication for supported Storage operations.
- Private Blob container access.
- HTTPS-only Storage traffic.
- TLS 1.2 minimum for Storage and Service Bus.
- Key Vault RBAC authorization and purge protection.

Some Azure services currently have public network endpoints enabled.
Network isolation and private endpoints require additional design and
validation before a production deployment.

## Terraform State

The intended production approach is an Azure Storage remote backend.

The remote backend is currently documented in `backend.tf` but is
not enabled.

Terraform uses the default local backend for development-time validation
and planning.

State files, plans, and local variable files must not be committed.

## Local Validation

Prerequisites:

1. Terraform CLI installed.
2. Azure CLI installed and authenticated.
3. Access to the target Azure subscription.
4. A local `terraform.tfvars` file based on `terraform.tfvars.example`.

Initialize Terraform without a remote backend:

```powershell
terraform init -backend=false -reconfigure
```

Format and validate:

```powershell
terraform fmt
terraform validate
```

Generate a plan:

```powershell
terraform plan -input=false
```

The current development configuration produces:

```text
Plan: 16 to add, 0 to change, 0 to destroy.
```

This is a planning result, not proof that Azure resource provisioning
or application integration will succeed.

## Deployment Prerequisites

Before any deployment:

1. Obtain explicit approval to provision billable Azure resources.
2. Configure and initialize the Azure Storage remote-state backend.
3. Confirm resource names and Azure service availability.
4. Verify required Azure RBAC permissions.
5. Verify Microsoft Entra ID access for Storage container management.
6. Replace placeholder container images with published images.
7. Configure PostgreSQL credentials securely.
8. Review networking, public access, and production security requirements.
9. Confirm Azure AI service quotas and regional availability.
10. Review estimated Azure costs and establish budget controls.
11. Test application connectivity, identity permissions, and observability.

## Current Validation Status

- Terraform initialization: passed.
- Terraform formatting: passed.
- Terraform validation: passed.
- Terraform plan: passed.
- Planned core resources: 16.
- Optional application resources: disabled.
- Optional PostgreSQL resources: disabled.
- Optional AI resources: disabled.
- Azure deployment: not performed.

## Project Policy

Azure infrastructure deployment is intentionally deferred until the
P0–P11 implementation milestones are complete.

Do not deploy infrastructure as part of the current P2 milestone.

**P2 status: Infrastructure-as-Code complete; deployment deferred.**
