#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
mode="${1:-linux}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
curl --fail --location --retry 3 https://downloads.es.net/pub/iperf/iperf-3.19.1.tar.gz -o "$work/source.tar.gz"
echo 'dc63f89ec581ea99f8b558d8eb35109de06383010db5a1906c208a562ba0c270  '"$work/source.tar.gz" | sha256sum -c -
tar -xzf "$work/source.tar.gz" -C "$work"
source="$work/iperf-3.19.1"
if [[ "$mode" == android ]]; then
  ndk="${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME}"
  toolchain="$ndk/toolchains/llvm/prebuilt/linux-x86_64/bin"
  for abi in arm64-v8a x86_64; do
    case "$abi" in arm64-v8a) target=aarch64-linux-android;; x86_64) target=x86_64-linux-android;; esac
    mkdir "$work/$abi"
    pushd "$work/$abi"
    CC="$toolchain/${target}26-clang" AR="$toolchain/llvm-ar" RANLIB="$toolchain/llvm-ranlib" CFLAGS='-O2 -fPIC' LDFLAGS='-Wl,-z,max-page-size=16384' "$source/configure" --host="$target" --disable-shared --enable-static --without-openssl
    make -j2 -C src libiperf.la
    output="$root/build/native/android/$abi"
    mkdir -p "$output"
    "$toolchain/${target}26-clang" -shared -O2 -fPIC -Wl,-z,max-page-size=16384 -I./src -I"$source/src" "$root/build/iperf/android-client.c" src/.libs/libiperf.a -lm -o "$output/librfloor_iperf.so"
    "$toolchain/llvm-strip" "$output/librfloor_iperf.so"
    popd
  done
else
  pushd "$source"
  ./configure --disable-shared --enable-static --without-openssl
  make -j2
  output="$root/build/native/linux"
  mkdir -p "$output"
  cp src/iperf3 "$output/iperf3"
  strip "$output/iperf3"
  "$output/iperf3" --version
  popd
fi
mkdir -p "$root/build/native/licenses"
cp "$source/LICENSE" "$root/build/native/licenses/iperf3-LICENSE"
