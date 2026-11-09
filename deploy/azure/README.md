# Deploying to Azure

An alternative to the [Google Cloud setup](../gcp/README.md), where the live demo runs. The difference: here attachments go to Azure Blob Storage instead of the Azurite container on the VM.

The stand is one Linux VM running docker compose (the same `docker-compose.yml` as locally) plus Azure Blob Storage for attachments. Caddy gets HTTPS from Let's Encrypt for the VM's DNS name (`<name>.<region>.cloudapp.azure.com`).

```
Internet ──443──► Caddy ──► API ──► SQL Server, Redis, RabbitMQ, Elasticsearch   (containers on the VM)
                              └───► Azure Blob Storage (private container "attachments")
                  Worker ──► SQL Server, RabbitMQ, Elasticsearch
```

Why not PaaS: SQL Server, Elasticsearch, RabbitMQ and Redis as managed Azure services cost an order of magnitude more than one VM, and for a demo it matters more that the cloud setup matches what runs locally. The path to production is in [docs/architecture.md](../../docs/architecture.md): Azure SQL, Azure Cache for Redis, Azure SignalR Service, Elastic Cloud, Container Apps.

## Prerequisites

- An Azure subscription (a new account gets a 30-day credit, enough for the stand).
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), after `az login`.
- Bash with `ssh`, `scp`, `git`, `openssl` (Git Bash on Windows).

## Steps

From the repository root:

```bash
deploy/azure/provision.sh
```

The script creates the resource group `comments-rg`, a storage account with a private `attachments` container and a `Standard_B2as_v2` VM (2 vCPU, 8 GB) with Ubuntu 24.04. On the VM's first boot `cloud-init` installs Docker, creates a swap file and sets `vm.max_map_count` for Elasticsearch. Region, name and size are variables: `LOCATION=northeurope NAME=my-comments VM_SIZE=Standard_B4as_v2 deploy/azure/provision.sh`.

The settings — the site address, the Blob Storage connection string and generated SQL Server and RabbitMQ passwords — go to `deploy/azure/.env`, which git ignores.

```bash
deploy/azure/deploy.sh
```

The script copies the current commit to the VM over SSH (`git archive`, so the repository does not have to be public), builds the images on the VM and runs:

```bash
docker compose -f docker-compose.yml -f deploy/azure/docker-compose.azure.yml --env-file deploy/azure/.env up -d --build
```

[`docker-compose.azure.yml`](docker-compose.azure.yml) adds Caddy (ports 80/443), switches attachments to Azure Blob Storage, drops the Azurite emulator, hides the API and the RabbitMQ console from the outside and limits container memory to fit 8 GB.

The first run takes 5–10 minutes (building images and issuing the certificate). Then the site opens at the address the script printed. Running `deploy.sh` again ships a new commit.

## Operations

```bash
source deploy/azure/.env
ssh azureuser@$SITE_ADDRESS
cd comments
docker compose -f docker-compose.yml -f deploy/azure/docker-compose.azure.yml --env-file deploy/azure/.env ps
docker compose -f docker-compose.yml -f deploy/azure/docker-compose.azure.yml --env-file deploy/azure/.env logs -f api worker
```

Delete everything (VM, disks, storage account, IP):

```bash
az group delete --name comments-rg --yes
```

## Cost

`Standard_B2as_v2` with a 64 GB disk and a public IP is about $60–70 a month; the storage account costs cents. A new account's credit covers about three months.
