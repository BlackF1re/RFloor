#!/usr/bin/env bash
set -euo pipefail
package=io.github.blackf1re.rfloor
output=artifacts/android-check
mkdir -p "$output"
trap 'adb logcat -d > "$output/logcat.txt"; adb exec-out screencap -p > "$output/final.png"' EXIT
apk="$(find artifacts/packages -name '*.apk' -print -quit)"
adb install -r "$apk"
adb shell pm grant "$package" android.permission.ACCESS_FINE_LOCATION
adb shell pm grant "$package" android.permission.ACCESS_COARSE_LOCATION
adb shell pm grant "$package" android.permission.NEARBY_WIFI_DEVICES
adb logcat -c
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1
sleep 10
adb logcat -d > "$output/logcat.txt"
adb shell pidof "$package"
if grep -E 'FATAL EXCEPTION|Fatal signal|Unhandled Exception|FATAL UNHANDLED EXCEPTION' "$output/logcat.txt"; then exit 1; fi
adb exec-out screencap -p > "$output/welcome.png"
adb uninstall "$package"
check_apk="$(find artifacts/android-instrumentation -name '*-Signed.apk' -print -quit)"
test -n "$check_apk"
adb install "$check_apk"
adb shell pm grant "$package" android.permission.ACCESS_FINE_LOCATION
adb shell pm grant "$package" android.permission.ACCESS_COARSE_LOCATION
adb shell pm grant "$package" android.permission.NEARBY_WIFI_DEVICES
adb shell settings put secure location_mode 3
adb shell svc wifi enable
adb logcat -c
activity="$(adb shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r')"
adb shell am start -W -n "$activity" --ez rfloor_platform_checks true
for attempt in $(seq 1 90); do
  adb logcat -d -s RFloorChecks:I > "$output/platform-checks.txt"
  if grep -q RFLOOR_PLATFORM_CHECKS_ "$output/platform-checks.txt"; then break; fi
  if adb logcat -d | grep -Eq 'FATAL EXCEPTION|Fatal signal|FATAL UNHANDLED EXCEPTION'; then break; fi
  sleep 1
done
cat "$output/platform-checks.txt"
grep -q RFLOOR_PLATFORM_CHECKS_PASSED "$output/platform-checks.txt"
adb exec-out screencap -p > "$output/editor.png"
adb logcat -d > "$output/logcat.txt"
