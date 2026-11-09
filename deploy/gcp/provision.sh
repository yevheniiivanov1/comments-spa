#!/usr/bin/env bash
# Creates the Google Cloud resources for the stand and writes deploy/gcp/.env:
# a static external IP, a firewall rule for HTTP/HTTPS and an Ubuntu VM that accepts a dedicated SSH key.
# Needs the Google Cloud CLI, logged in (`gcloud auth login`), and a project with billing. Run from the repository root.
# Safe to run again: existing resources and an existing .env (with the database password) are kept.
set -euo pipefail

PROJECT=${PROJECT:-$(gcloud config get-value project 2>/dev/null | tr -d '\r')}
REGION=${REGION:-europe-central2}
ZONE=${ZONE:-$REGION-a}
NAME=${NAME:-comments}
MACHINE_TYPE=${MACHINE_TYPE:-e2-standard-2}
SSH_KEY=${SSH_KEY:-$HOME/.ssh/comments-gcp}
ENV_FILE=deploy/gcp/.env

if [ -z "$PROJECT" ]; then
  echo "No project: run 'gcloud config set project <id>' or set PROJECT." >&2
  exit 1
fi
g() { gcloud --project "$PROJECT" --quiet "$@"; }

echo "Project $PROJECT, zone $ZONE"
g services enable compute.googleapis.com

if [ ! -f "$SSH_KEY" ]; then
  echo "SSH key $SSH_KEY"
  mkdir -p "$(dirname "$SSH_KEY")"
  ssh-keygen -t ed25519 -N "" -C deploy -f "$SSH_KEY" -q
fi

if ! g compute addresses describe "$NAME-ip" --region "$REGION" > /dev/null 2>&1; then
  echo "Static IP $NAME-ip"
  g compute addresses create "$NAME-ip" --region "$REGION"
fi
IP=$(g compute addresses describe "$NAME-ip" --region "$REGION" --format 'value(address)' | tr -d '\r')

if ! g compute firewall-rules describe "$NAME-web" > /dev/null 2>&1; then
  echo "Firewall rule $NAME-web (80, 443)"
  g compute firewall-rules create "$NAME-web" --network default --direction INGRESS \
    --allow tcp:80,tcp:443,udp:443 --source-ranges 0.0.0.0/0 --target-tags "$NAME-web"
fi

if ! g compute instances describe "$NAME-vm" --zone "$ZONE" > /dev/null 2>&1; then
  echo "VM $NAME-vm ($MACHINE_TYPE)"
  g compute instances create "$NAME-vm" --zone "$ZONE" --machine-type "$MACHINE_TYPE" \
    --image-family ubuntu-2404-lts-amd64 --image-project ubuntu-os-cloud \
    --boot-disk-size 64GB --boot-disk-type pd-balanced \
    --address "$IP" --tags "$NAME-web" \
    --metadata-from-file user-data=deploy/gcp/cloud-init.yaml \
    --metadata "enable-oslogin=FALSE,ssh-keys=deploy:$(cat "$SSH_KEY.pub")"
fi

if [ ! -f "$ENV_FILE" ]; then
  umask 077
  cat > "$ENV_FILE" <<EOF
SERVER_IP=$IP
SITE_ADDRESS=${IP//./-}.nip.io
SQL_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')Aa1!
RABBITMQ_USER=comments
RABBITMQ_PASSWORD=$(openssl rand -hex 16)
EOF
fi

echo "Done. Settings are in $ENV_FILE (not committed). Next: deploy/gcp/deploy.sh"
echo "Site: https://$(grep '^SITE_ADDRESS=' "$ENV_FILE" | cut -d= -f2)"
