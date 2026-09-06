#!/bin/bash
#
# generate-ssl-cert.sh — генерация TLS-сертификатов для nginx.
#
# Результат в ssl/:
#   fullchain.pem  — серверный сертификат + CA (то, что ждёт nginx)
#   private.key    — приватный ключ сервера
#   root.crt       — CA-сертификат (для повторного использования)
#   root.key       — приватный ключ CA
#

set -euo pipefail

SCRIPT_DIR="$(dirname "$(readlink -f "$0")")"

# ---------------------------------------------------------------------------
# 1. Источник домена — парсим ANALYTICS_AUTHLINK_BASE_URL из .env
# ---------------------------------------------------------------------------
if [ -f "$SCRIPT_DIR/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    source "$SCRIPT_DIR/.env"
    set +a
fi

if [ -z "${ANALYTICS_AUTHLINK_BASE_URL:-}" ]; then
    echo "ERROR: ANALYTICS_AUTHLINK_BASE_URL is not set in .env" >&2
    exit 1
fi

# Извлекаем домен: убираем схему (http:// / https://), порт и путь
dns=$(echo "$ANALYTICS_AUTHLINK_BASE_URL" \
    | sed -E 's#^https?://##' \
    | sed -E 's#[:/].*##')

if [ -z "$dns" ]; then
    echo "ERROR: could not extract domain from ANALYTICS_AUTHLINK_BASE_URL='$ANALYTICS_AUTHLINK_BASE_URL'" >&2
    exit 1
fi

echo "Domain for certificate: $dns"

# ---------------------------------------------------------------------------
# 2. Пути
# ---------------------------------------------------------------------------
ssl_path="${SCRIPT_DIR}/ssl"
mkdir -p "$ssl_path"

key="$ssl_path/private.key"
fullchain="$ssl_path/fullchain.pem"
ca_key="$ssl_path/root.key"
ca_cert="$ssl_path/root.crt"
csr_config="$ssl_path/csr.conf"
csr="$ssl_path/certificate.csr"
cert_conf="$ssl_path/cert.conf"
cert_tmp="$ssl_path/certificate.crt"

# ---------------------------------------------------------------------------
# 3. CA: генерируем, если отсутствует; иначе переиспользуем
# ---------------------------------------------------------------------------
if [ ! -f "$ca_key" ] || [ ! -f "$ca_cert" ]; then
    echo "Generating new CA..."
    openssl req -x509 \
        -sha256 -days 365 \
        -nodes \
        -newkey rsa:2048 \
        -subj "/CN=$dns/C=RU/L=Moscow" \
        -keyout "$ca_key" -out "$ca_cert"
else
    echo "Reusing existing CA: $ca_cert"
fi

# Проверяем, что CA-сертификат валиден (не просрочен)
if ! openssl x509 -checkend 0 -noout -in "$ca_cert" 2>/dev/null; then
    echo "ERROR: CA certificate $ca_cert is expired. Remove it and re-run." >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# 4. Генерируем серверный ключ
# ---------------------------------------------------------------------------
echo "Generating server key..."
openssl genrsa -out "$key" 2048

# ---------------------------------------------------------------------------
# 5. CSR-конфиг и запрос на подпись
# ---------------------------------------------------------------------------
cat > "$csr_config" <<EOF
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

echo "Creating CSR..."
openssl req -new -key "$key" -out "$csr" -config "$csr_config"

# ---------------------------------------------------------------------------
# 6. Подписываем сертификат через локальный CA
# ---------------------------------------------------------------------------
cat > "$cert_conf" <<EOF
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = $dns

EOF

echo "Signing certificate..."
openssl x509 -req \
    -in "$csr" \
    -CA "$ca_cert" -CAkey "$ca_key" \
    -CAcreateserial -out "$cert_tmp" \
    -days 365 \
    -sha256 -extfile "$cert_conf"

# ---------------------------------------------------------------------------
# 7. fullchain.pem = серверный сертификат + CA
# ---------------------------------------------------------------------------
echo "Creating fullchain.pem..."
cat "$cert_tmp" "$ca_cert" > "$fullchain"

# ---------------------------------------------------------------------------
# 8. Удаляем промежуточные артефакты
# ---------------------------------------------------------------------------
rm -f "$csr_config" "$cert_conf" "$csr" "$cert_tmp"
# Артефакты предыдущих версий скрипта (если остались в ssl/): старый формат
# вывода certificate.crt. root.srl намеренно сохраняется — это серийник CA.

echo "Done. Generated files in $ssl_path:"
echo "  fullchain.pem  — ssl certificate chain (server + CA)"
echo "  private.key    — server private key"
echo "  root.crt       — CA certificate"
echo "  root.key       — CA private key"
