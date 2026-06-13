#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "${script_dir}/.." && pwd)"

out_root="${repo_root}/build/out"
initramfs_root="${out_root}/initramfs-root"
init_binary="${out_root}/init"
initctl_binary="${out_root}/initctl"
service_manager_binary="${out_root}/system/ServiceManager"
shell_binary="${out_root}/system/Shell"
device_manager_binary="${out_root}/system/devd"
apps_root="${out_root}/Apps"
initramfs_image="${INITRAMFS:-${out_root}/initramfs.cpio.gz}"
initramfs_tmp="${initramfs_image}.tmp"

mkdir -p "${out_root}" "$(dirname -- "${initramfs_image}")"
rm -rf "${initramfs_root}"
rm -f "${initramfs_tmp}"
mkdir -p \
  "${initramfs_root}/dev" \
  "${initramfs_root}/proc" \
  "${initramfs_root}/sys" \
  "${initramfs_root}/run" \
  "${initramfs_root}/tmp" \
  "${initramfs_root}/system" \
  "${initramfs_root}/system/services" \
  "${initramfs_root}/Apps"

echo "Building static /init..."
gcc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -O2 \
  -static \
  -o "${init_binary}" \
  "${repo_root}/src/Init/init.c"

echo "Building static /system/initctl..."
gcc \
  -std=c11 \
  -Wall \
  -Wextra \
  -Werror \
  -O2 \
  -static \
  -o "${initctl_binary}" \
  "${repo_root}/src/Init/initctl.c"

echo "Building ServiceManager..."
bash "${script_dir}/build-dotnet.sh"

cp "${init_binary}" "${initramfs_root}/init"
cp "${initctl_binary}" "${initramfs_root}/system/initctl"
cp "${service_manager_binary}" "${initramfs_root}/system/ServiceManager"
cp "${shell_binary}" "${initramfs_root}/system/Shell"
cp "${device_manager_binary}" "${initramfs_root}/system/devd"
cp -a "${apps_root}/." "${initramfs_root}/Apps/"
chmod 0755 "${initramfs_root}/init"
chmod 0755 "${initramfs_root}/system/initctl"
chmod 0755 "${initramfs_root}/system/ServiceManager"
chmod 0755 "${initramfs_root}/system/Shell"
chmod 0755 "${initramfs_root}/system/devd"
chmod 0755 "${initramfs_root}"
chmod 0755 "${initramfs_root}/dev" "${initramfs_root}/proc" "${initramfs_root}/sys" "${initramfs_root}/run" "${initramfs_root}/tmp" "${initramfs_root}/system" "${initramfs_root}/system/services" "${initramfs_root}/Apps"

cat > "${initramfs_root}/system/services/01-devd.service.json" <<EOF
{
  "name": "devd",
  "exec": "/system/devd",
  "restart": "always",
  "critical": true
}
EOF

cat > "${initramfs_root}/system/services/10-Shell.service.json" <<EOF
{
  "name": "Shell",
  "exec": "/system/Shell",
  "restart": "always",
  "critical": true
}
EOF

copy_runtime_dependency() {
  local dependency="$1"
  local target="${initramfs_root}${dependency}"

  if [[ ! -f "${dependency}" ]]; then
    return
  fi

  mkdir -p "$(dirname -- "${target}")"
  cp -L "${dependency}" "${target}"
  chmod 0755 "${target}" || true
}

copy_ldd_dependencies() {
  local binary="$1"
  local dependency

  while IFS= read -r line; do
    if [[ "${line}" =~ \=\>\ / ]]; then
      dependency="${line#*=> }"
      dependency="${dependency%% (*}"
    elif [[ "${line}" =~ ^[[:space:]]*/ ]]; then
      dependency="${line#"${line%%[![:space:]]*}"}"
      dependency="${dependency%% (*}"
    else
      continue
    fi

    copy_runtime_dependency "${dependency}"
  done < <(ldd "${binary}" 2>/dev/null || true)
}

copy_managed_app_runtime_dependencies() {
  local bin_dir="$1"
  local native_file

  while IFS= read -r native_file; do
    copy_ldd_dependencies "${native_file}"
  done < <(
    find "${bin_dir}" -maxdepth 1 -type f \( \
      -perm -0100 -o \
      -name "*.so" -o \
      -name "*.so.*" \
    \)
  )
}

copy_managed_app_runtime_dependencies "${initramfs_root}/Apps/HWorld.app/bin"

echo "Creating initramfs..."
(
  cd "${initramfs_root}"
  find . -print0 | cpio --null --quiet -o --format=newc
) | gzip -9 > "${initramfs_tmp}"

mv "${initramfs_tmp}" "${initramfs_image}"

echo "Initramfs written to ${initramfs_image}"
if command -v file >/dev/null 2>&1; then
  if file "${init_binary}" | grep -q "statically linked"; then
    echo "/init is statically linked."
  else
    echo "warning: /init does not appear to be statically linked." >&2
  fi
fi
