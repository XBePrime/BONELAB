<div align="center">

# NERVE

**Bare-hand control for BONELAB — Quest hand tracking → full finger sync**

Put the controllers down. Your hands drive the rig.

<br/>

<img src="https://img.shields.io/badge/%20-v1.0.0-F97316?style=for-the-badge" height="40" />
&nbsp;
<img src="https://img.shields.io/badge/%20-Author_BE%20PRIME-111827?style=for-the-badge" height="40" />
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Telegram_%40be__primex-26A5E4?style=for-the-badge&logo=telegram&logoColor=white" height="40" /></a>

<br/><br/>

<a href="https://github.com/XBePrime/BONELAB/releases/tag/nerve-v1.0.0"><img src="https://img.shields.io/badge/%20-Release_v1.0.0-F97316?style=for-the-badge&logo=github&logoColor=white" height="48" /></a>
&nbsp;
<a href="https://github.com/XBePrime/BONELAB/releases/download/nerve-v1.0.0/Nerve.dll"><img src="https://img.shields.io/badge/%20-Download_DLL-16A34A?style=for-the-badge&logo=dotnet&logoColor=white" height="48" /></a>
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Support-FF0033?style=for-the-badge&logo=telegram&logoColor=white" height="48" /></a>

</div>

---

## Required once — APK hand tracking

Quest will **not** expose hands to BONELAB unless the APK declares Meta hand tracking.
Without this, the log stays at `handData=0` / `ovrEnable=False` forever.

1. Download **HandTrackApkPatch.zip** from the [v1.0.0 release](https://github.com/XBePrime/BONELAB/releases/tag/nerve-v1.0.0)
2. Run it on your BONELAB APK → `*.handtrack.apk`
3. Reinstall / re-patch with LemonLoader (re-sign)
4. Allow **Hand Tracking** if Quest prompts

```bash
dotnet HandTrackApkPatch.dll /path/to/BONELAB.apk
```

---

## What it does

| | |
|---|---|
| **Hand tracking** | Meta Quest 3 / 3S — Unity XR + OVRPlugin bridge |
| **Per-finger sync** | Thumb · Index · Middle · Ring · Pinky |
| **Wrist** | Follows tracked palm |
| **Grip** | Pinch / fist → grab |
| **Walk** | Point + pinch thumb/index → move |

---

## Menu

```text
BoneMenu → NERVE
```

| Option | Default |
|--------|---------|
| Enabled | On |
| Sync Wrist | On |
| Sync Bones | Off (enable if needed) |
| Full Skeleton | Off (enable if needed) |
| Grip From Fingers | On |
| Pinch Walk | On |

---

## How to use

1. Finish the APK patch above
2. `Nerve.dll` → `Mods/`
3. **BoneMenu → NERVE → Enabled**
4. Controllers down → fingers drive the avatar
5. **Walk:** point (index open) + pinch thumb → move
6. **Fist** = grab only (does not walk)

---

## Install

1. MelonLoader / LemonLoader  
2. [BoneLib](https://thunderstore.io/c/bonelab/p/gnonme/BoneLib/)  
3. Patch APK with **HandTrackApkPatch** (once)  
4. `Nerve.dll` → `Mods/`  
5. **BoneMenu → NERVE**

Verify in MelonLoader log: `handData>0` / `liveL=True` with controllers down.

<div align="center">

<br/>

<a href="https://github.com/XBePrime/BONELAB/releases/download/nerve-v1.0.0/Nerve.dll"><img src="https://img.shields.io/badge/%20-Get_the_DLL-111827?style=for-the-badge&logo=dotnet&logoColor=white" height="44" /></a>
&nbsp;
<a href="https://thunderstore.io/c/bonelab/p/gnonme/BoneLib/"><img src="https://img.shields.io/badge/%20-BoneLib-F59E0B?style=for-the-badge" height="44" /></a>
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Telegram-0284C7?style=for-the-badge&logo=telegram&logoColor=white" height="44" /></a>

<br/><br/>

**BE PRIME** · [@be_primex](https://t.me/be_primex)

</div>
