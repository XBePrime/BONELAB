# HandTrackApkPatch

Quest **will not** give BONELAB hand devices unless the APK declares:

```xml
<uses-permission android:name="com.oculus.permission.HAND_TRACKING" />
<uses-feature android:name="oculus.software.handtracking" android:required="false" />
```

Without these, NERVE sees `ovrEnable=False` + `handData=0` forever. A Melon mod cannot add them at runtime.

## Patch once

```bash
# pull or copy BONELAB base APK, then:
dotnet HandTrackApkPatch.dll /path/to/BONELAB.apk
# → writes BONELAB.handtrack.apk
```

Then re-sign / reinstall (LemonLoader re-patch of the patched APK, or `apksigner`).

On first launch, allow **Hand Tracking** if Quest asks.

## Verify

MelonLoader log should show `handChar>0` / `handData>0` / `liveL=True` after controllers are down.
