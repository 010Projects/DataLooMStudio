#!/bin/sh
set -eu

required_variables='DLS_ENTRA_AUTHORITY DLS_ENTRA_TENANT_ID DLS_SPA_CLIENT_ID DLS_API_SCOPE DLS_API_ORIGIN'
for variable_name in $required_variables; do
  eval "variable_value=\${$variable_name:-}"
  if [ -z "$variable_value" ]; then
    echo "Required public runtime variable $variable_name is not configured." >&2
    exit 1
  fi
done

envsubst '${DLS_ENTRA_AUTHORITY} ${DLS_ENTRA_TENANT_ID} ${DLS_SPA_CLIENT_ID} ${DLS_API_SCOPE}' \
  < /usr/share/nginx/templates/config.js.template \
  > /usr/share/nginx/html/config.js
