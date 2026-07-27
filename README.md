<div align="center">

<img src="https://raw.githubusercontent.com/XBePrime/BONELAB/media/icon.png" alt="NERVE" width="148" />

# NERVE

**Bare-hand control for BONELAB — Quest hand tracking → full finger sync**

Put the controllers down. Your hands drive the rig. Every finger.

<br/>

<img src="https://img.shields.io/badge/%20-v1.0.0-F97316?style=for-the-badge" height="40" />
&nbsp;
<img src="https://img.shields.io/badge/%20-Author_BE%20PRIME-111827?style=for-the-badge" height="40" />
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Telegram_%40be__primex-26A5E4?style=for-the-badge&logo=telegram&logoColor=white" height="40" /></a>

<br/><br/>

<a href="Nerve.dll"><img src="https://img.shields.io/badge/%20-Download_DLL-16A34A?style=for-the-badge&logo=dotnet&logoColor=white" height="48" /></a>
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Support-FF0033?style=for-the-badge&logo=telegram&logoColor=white" height="48" /></a>

</div>

---

## What it does

| | |
|---|---|
| **Hand tracking** | Meta Quest 3 / 3S (OpenXR / Oculus hands) |
| **Per-finger sync** | Thumb · Index · Middle · Ring · Pinky — 1:1 curls |
| **Gestures** | Flip-off, fist, OK, point — rig mirrors you |
| **Wrist** | Hands replace controller pose when controllers are down |
| **Grip** | Pinch / fist maps to grab input |
| **Latency** | Direct overwrite after `OpenController.OnUpdate` — no smoothing filter |

---

## Menu

```text
BoneMenu → NERVE
```

| Option | Default |
|--------|---------|
| Enabled | On |
| Sync Wrist | On |
| Sync Bones | On |
| Full Skeleton | On |
| Grip From Fingers | On |

---

## How to use

1. Enable **BoneMenu → NERVE → Enabled**
2. Set controllers down (Quest hand tracking kicks in)
3. Move your fingers — the avatar hands follow

Works best on **Meta Quest 3 / 3S** with hand tracking enabled in headset settings.

---

## Install

1. MelonLoader / LemonLoader  
2. [BoneLib](https://thunderstore.io/c/bonelab/p/gnonme/BoneLib/)  
3. `Nerve.dll` → `Mods/`  
4. **BoneMenu → NERVE**

<div align="center">

<br/>

<a href="Nerve.dll"><img src="https://img.shields.io/badge/%20-Get_the_DLL-111827?style=for-the-badge&logo=dotnet&logoColor=white" height="44" /></a>
&nbsp;
<a href="https://thunderstore.io/c/bonelab/p/gnonme/BoneLib/"><img src="https://img.shields.io/badge/%20-BoneLib-F59E0B?style=for-the-badge" height="44" /></a>
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Telegram-0284C7?style=for-the-badge&logo=telegram&logoColor=white" height="44" /></a>

<br/><br/>

**BE PRIME** · [@be_primex](https://t.me/be_primex)

</div>
