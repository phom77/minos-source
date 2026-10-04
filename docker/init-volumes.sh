#!/bin/sh
set -eu
umask 077

# Only these Compose-owned Linux volumes are initialized, never repository paths.
chown 1654:1654 /private /workspaces /certs
chmod 700 /private /workspaces /certs
if [ ! -f /certs/ca.crt ]; then
    openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 3650 \
        -subj '/CN=Minos Local Development CA' \
        -addext 'basicConstraints=critical,CA:TRUE' \
        -addext 'keyUsage=critical,keyCertSign,cRLSign' \
        -keyout /certs/ca.key -out /certs/ca.crt >/dev/null 2>&1
fi
if [ ! -f /certs/localhost.crt ] || ! openssl x509 -checkend 2592000 -noout -in /certs/localhost.crt >/dev/null; then
    openssl req -new -newkey rsa:2048 -nodes -sha256 -subj '/CN=localhost' \
        -keyout /certs/localhost.key -out /certs/localhost.csr >/dev/null 2>&1
    cat > /certs/localhost.ext <<'EOF'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,DNS:api,IP:127.0.0.1
EOF
    openssl x509 -req -sha256 -days 365 -in /certs/localhost.csr \
        -CA /certs/ca.crt -CAkey /certs/ca.key -CAcreateserial \
        -extfile /certs/localhost.ext -out /certs/localhost.crt >/dev/null 2>&1
    rm -f /certs/localhost.csr /certs/localhost.ext
fi
chown 1654:1654 /certs/*
chmod 600 /certs/*
echo 'Private volumes and local HTTPS certificates are ready.'
