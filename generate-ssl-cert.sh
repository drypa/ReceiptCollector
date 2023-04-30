#!/bin/bash

set -e
source .env

dns=$BACKEND_GRPC_HOST

SCRIPT_DIR="$(dirname "$(readlink -f "$0")")"
ssl_path="${SCRIPT_DIR}/ssl"
key="$ssl_path/private.key"
cert="$ssl_path/certificate.crt"
csr_config="$ssl_path/csr.conf"
csr="$ssl_path/certificate.csr"
cert_conf="$ssl_path/cert.conf"
ca_key="$ssl_path/root.key"
ca_cert="$ssl_path/root.crt"

openssl req -x509 \
            -sha256 -days 356 \
            -nodes \
            -newkey rsa:2048 \
            -subj "/CN=$dns/C=RU/L=Moscow" \
            -keyout $ca_key -out $ca_cert


#generate key
openssl genrsa -out $key 2048

cat > $csr_config <<EOF
[ req ]
default_bits = 2048
prompt = no
default_md = sha256
req_extensions = req_ext
distinguished_name = dn

[ dn ]
C = RU
ST = Moscow
L = Moscow
O = DRypa
OU = DRypa Dev
CN = $dns

[ req_ext ]
subjectAltName = @alt_names

[ alt_names ]
DNS.1 = $dns
DNS.2 = localhost
IP.1 = 127.0.0.1

EOF

openssl req -new -key $key -out $csr -config $csr_config

cat > $cert_conf <<EOF

authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = $dns

EOF

openssl x509 -req \
    -in $csr \
    -CA $ca_cert -CAkey $ca_key \
    -CAcreateserial -out $cert \
    -days 365 \
    -sha256 -extfile $cert_conf


rm $csr_config
rm $cert_conf
rm $csr
rm $ca_key
rm $ca_cert