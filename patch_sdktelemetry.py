import zipfile, shutil, os, sys

OLD = b'package="com.oculus.Integration"'
NEW = b'package="com.oculus.sdktelemetry"'

aars = [
    r"D:/Games/TheVortexVr/Library/PackageCache/com.meta.xr.sdk.voice@f4180b2de811/Lib/Telemetry/Plugins/SDKTelemetry.aar",
    r"D:/Games/TheVortexVr/Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/libs/SDKTelemetry.aar",
]

def patch(aar):
    if not os.path.exists(aar):
        print("SKIP (missing):", aar); return
    bak = aar + ".orig"
    if not os.path.exists(bak):
        shutil.copy2(aar, bak)
        print("backup ->", bak)
    tmp = aar + ".tmp"
    changed = False
    with zipfile.ZipFile(aar, "r") as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "AndroidManifest.xml" and OLD in data:
                data = data.replace(OLD, NEW)
                changed = True
            zout.writestr(item, data)
    os.replace(tmp, aar)
    print(("PATCHED " if changed else "no-op  "), aar)

for a in aars:
    patch(a)
print("DONE")
