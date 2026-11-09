#!/usr/bin/env bash
# Ships the current commit to the VM created by provision.sh and (re)starts the stack there.
# The code goes over SSH (git archive), so the repository does not need to be public. Run from the repository root.
set -euo pipefail

source deploy/gcp/.env
SSH_KEY=${SSH_KEY:-$HOME/.ssh/comments-gcp}
HOST="deploy@$SERVER_IP"
SSH_OPTS=(-i "$SSH_KEY" -o StrictHostKeyChecking=accept-new)
COMPOSE="sudo docker compose -f docker-compose.yml -f deploy/gcp/docker-compose.gcp.yml --env-file deploy/gcp/.env"

echo "Waiting for the VM to finish its first boot…"
ssh "${SSH_OPTS[@]}" "$HOST" 'cloud-init status --wait > /dev/null; mkdir -p ~/comments'

echo "Copying $(git rev-parse --short HEAD)…"
git archive --format=tar HEAD | ssh "${SSH_OPTS[@]}" "$HOST" 'rm -rf ~/comments.new && mkdir ~/comments.new && tar -x -C ~/comments.new && rsync -a --delete --exclude deploy/gcp/.env ~/comments.new/ ~/comments/ && rm -rf ~/comments.new'
scp -q "${SSH_OPTS[@]}" deploy/gcp/.env "$HOST:comments/deploy/gcp/.env"

echo "Building and starting…"
ssh "${SSH_OPTS[@]}" "$HOST" "cd ~/comments && $COMPOSE up -d --build --remove-orphans && $COMPOSE ps"

echo "https://$SITE_ADDRESS"
