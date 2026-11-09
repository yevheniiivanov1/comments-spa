#!/usr/bin/env bash
# Ships the current commit to the VM created by provision.sh and (re)starts the stack there.
# The code goes over SSH (git archive), so the repository does not need to be public. Run from the repository root.
set -euo pipefail

source deploy/azure/.env
HOST="azureuser@$SITE_ADDRESS"
COMPOSE="docker compose -f docker-compose.yml -f deploy/azure/docker-compose.azure.yml --env-file deploy/azure/.env"

echo "Waiting for the VM to finish its first boot…"
ssh -o StrictHostKeyChecking=accept-new "$HOST" 'cloud-init status --wait > /dev/null; mkdir -p ~/comments'

echo "Copying $(git rev-parse --short HEAD)…"
git archive --format=tar HEAD | ssh "$HOST" 'rm -rf ~/comments.new && mkdir ~/comments.new && tar -x -C ~/comments.new && rsync -a --delete --exclude deploy/azure/.env ~/comments.new/ ~/comments/ && rm -rf ~/comments.new'
scp -q deploy/azure/.env "$HOST:comments/deploy/azure/.env"

echo "Building and starting…"
ssh "$HOST" "cd ~/comments && $COMPOSE up -d --build --remove-orphans && $COMPOSE ps"

echo "https://$SITE_ADDRESS"
