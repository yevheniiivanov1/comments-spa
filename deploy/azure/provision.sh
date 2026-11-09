#!/usr/bin/env bash
# Creates the Azure resources for the stand and writes deploy/azure/.env:
# a resource group, a storage account with a private "attachments" container, and an Ubuntu VM with a public DNS name.
# Needs the Azure CLI, logged in (`az login`). Run from the repository root.
set -euo pipefail

LOCATION=${LOCATION:-westeurope}
RESOURCE_GROUP=${RESOURCE_GROUP:-comments-rg}
NAME=${NAME:-comments-$(openssl rand -hex 3)}
VM_SIZE=${VM_SIZE:-Standard_B2as_v2}
STORAGE=$(echo "${NAME//-/}files" | cut -c1-24)

echo "Resource group $RESOURCE_GROUP in $LOCATION"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "Storage account $STORAGE"
az storage account create --resource-group "$RESOURCE_GROUP" --name "$STORAGE" --location "$LOCATION" \
  --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false --output none
BLOB_CONNECTION_STRING=$(az storage account show-connection-string --resource-group "$RESOURCE_GROUP" --name "$STORAGE" --output tsv)
az storage container create --name attachments --connection-string "$BLOB_CONNECTION_STRING" --output none

echo "VM $NAME-vm ($VM_SIZE)"
az vm create --resource-group "$RESOURCE_GROUP" --name "$NAME-vm" --image Ubuntu2404 --size "$VM_SIZE" \
  --admin-username azureuser --generate-ssh-keys --public-ip-sku Standard --public-ip-address-dns-name "$NAME" \
  --os-disk-size-gb 64 --custom-data deploy/azure/cloud-init.yaml --output none
az vm open-port --resource-group "$RESOURCE_GROUP" --name "$NAME-vm" --port 80,443 --priority 900 --output none
SITE_ADDRESS=$(az vm show --show-details --resource-group "$RESOURCE_GROUP" --name "$NAME-vm" --query fqdns --output tsv)

umask 077
cat > deploy/azure/.env <<EOF
SITE_ADDRESS=$SITE_ADDRESS
BLOB_CONNECTION_STRING=$BLOB_CONNECTION_STRING
SQL_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')Aa1!
RABBITMQ_USER=comments
RABBITMQ_PASSWORD=$(openssl rand -hex 16)
EOF

echo "Done. Settings are in deploy/azure/.env (not committed). Next: deploy/azure/deploy.sh"
echo "Site: https://$SITE_ADDRESS"
