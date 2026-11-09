# Deploying to Google Cloud

The stand is one Compute Engine VM running docker compose — the same `docker-compose.yml` as locally, attachments included (Azurite keeps them on the VM's disk). Caddy gets HTTPS from Let's Encrypt for an [sslip.io](https://sslip.io) name that resolves to the VM's static IP (`34-118-1-2.sslip.io` → `34.118.1.2`), so no domain is needed.

```
Internet ──443──► Caddy ──► API ──► SQL Server, Redis, RabbitMQ, Elasticsearch, Azurite   (containers on the VM)
                  Worker ──► SQL Server, RabbitMQ, Elasticsearch
```

Why not managed services: SQL Server, Elasticsearch, RabbitMQ and Redis as managed services cost an order of magnitude more than one VM, and for a demo it matters more that the cloud setup matches what runs locally. The path to production is in [docs/architecture.md](../../docs/architecture.md).

## Prerequisites

- A Google Cloud project with billing (a new account gets a 90-day credit, enough for the stand).
- The [Google Cloud CLI](https://cloud.google.com/sdk/docs/install), after `gcloud auth login` and `gcloud config set project <id>`.
- Bash with `ssh`, `scp`, `ssh-keygen`, `git`, `openssl` (Git Bash on Windows).

## Steps

From the repository root:

```bash
deploy/gcp/provision.sh
```

The script enables the Compute Engine API and creates a static IP `comments-ip`, a firewall rule `comments-web` (TCP 80, 443 and UDP 443 for HTTP/3) and an `e2-standard-2` VM (2 vCPU, 8 GB) with Ubuntu 24.04 and a 64 GB disk in `europe-central2-a`. It generates a dedicated SSH key `~/.ssh/comments-gcp` and puts it into the VM's metadata for the user `deploy`. On the VM's first boot `cloud-init` installs Docker, creates a swap file and sets `vm.max_map_count` for Elasticsearch. Region, name and size are variables: `REGION=europe-west1 NAME=my-comments MACHINE_TYPE=e2-standard-4 deploy/gcp/provision.sh`.

The settings — the IP, the site address and generated SQL Server and RabbitMQ passwords — go to `deploy/gcp/.env`, which git ignores. Running the script again keeps existing resources and the existing `.env`: the database volume is initialized with that password.

```bash
deploy/gcp/deploy.sh
```

The script copies the current commit to the VM over SSH (`git archive`, so the repository does not have to be public), builds the images on the VM and runs:

```bash
sudo docker compose -f docker-compose.yml -f deploy/gcp/docker-compose.gcp.yml --env-file deploy/gcp/.env up -d --build
```

[`docker-compose.gcp.yml`](docker-compose.gcp.yml) adds Caddy (ports 80/443), hides the API and the RabbitMQ console from the outside and limits container memory to fit 8 GB.

The first run takes 5–10 minutes (building images and issuing the certificate). Then the site opens at the address the script printed. Running `deploy.sh` again ships a new commit.

## Operations

```bash
source deploy/gcp/.env
ssh -i ~/.ssh/comments-gcp deploy@$SERVER_IP
cd comments
sudo docker compose -f docker-compose.yml -f deploy/gcp/docker-compose.gcp.yml --env-file deploy/gcp/.env ps
sudo docker compose -f docker-compose.yml -f deploy/gcp/docker-compose.gcp.yml --env-file deploy/gcp/.env logs -f api worker
```

Stop paying for compute while keeping the disk and the IP (`start` brings it back with the same address):

```bash
gcloud compute instances stop comments-vm --zone europe-central2-a
```

Delete everything:

```bash
gcloud compute instances delete comments-vm --zone europe-central2-a --quiet
gcloud compute addresses delete comments-ip --region europe-central2 --quiet
gcloud compute firewall-rules delete comments-web --quiet
```

## Cost

`e2-standard-2` with a 64 GB balanced disk and a static IP is about $60 a month; the new-account credit covers the 90 days it lasts.
