<div align="center">

<img src="https://raw.githubusercontent.com/XBePrime/BONELAB/media/icon.png" alt="Aimbot" width="148" />

# Aimbot

**Precision aim assist for BONELAB**

NPCs · LabFusion players · BoneMenu

<br/>

<img src="https://img.shields.io/badge/%20-v1.1.0-E11D48?style=for-the-badge" height="40" />
&nbsp;
<img src="https://img.shields.io/badge/%20-Author_BE%20PRIME-111827?style=for-the-badge" height="40" />
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Telegram_%40be__primex-26A5E4?style=for-the-badge&logo=telegram&logoColor=white" height="40" /></a>

<br/><br/>

<a href="https://github.com/XBePrime/BONELAB/releases/tag/v1.1.0"><img src="https://img.shields.io/badge/%20-Release_v1.1.0-E11D48?style=for-the-badge&logo=github&logoColor=white" height="48" /></a>
&nbsp;
<a href="Aimbot.dll"><img src="https://img.shields.io/badge/%20-Download_DLL-16A34A?style=for-the-badge&logo=dotnet&logoColor=white" height="48" /></a>
&nbsp;
<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Support-FF0033?style=for-the-badge&logo=telegram&logoColor=white" height="48" /></a>

</div>

---

## What it does

| | |
|---|---|
| **Aimbot** | Snaps aim on fire inside FOV |
| **Triggerbot** | Fires when already on a valid target |
| **Bones** | Head · Chest · Closest |
| **NPCs** | Supported |
| **Players** | LabFusion (you are ignored) |
| **Lead / Drop** | Movement + bullet drop compensation |

---

## Menu

```text
BoneMenu → AIMBOT
```

| Option | Default |
|--------|---------|
| Aimbot | On |
| Aimbot FOV | 180 |
| Target | Head |
| Target NPCs | On |
| Target Players | On |
| Triggerbot | Off |
| Headshots Only | Off |

---

## Install

1. MelonLoader / LemonLoader  
2. [BoneLib](https://thunderstore.io/c/bonelab/p/gnonme/BoneLib/)  
3. [LabFusion](https://thunderstore.io/c/bonelab/p/Lakatrazz/LabFusion/) for players  
4. `Aimbot.dll` → `Mods/`  
5. **BoneMenu → AIMBOT**

<div align="center">

<br/>

<a href="Aimbot.dll"><img src="https://img.shields.io/badge/%20-Get_the_DLL-111827?style=for-the-badge&logo=dotnet&logoColor=white" height="44" /></a>
&nbsp;
<a href="https://thunderstore.io/c/bonelab/p/gnonme/BoneLib/"><img src="https://img.shields.io/badge/%20-BoneLib-F59E0B?style=for-the-badge" height="44" /></a>
&nbsp;
<a href="https://thunderstore.io/c/bonelab/p/Lakatrazz/LabFusion/"><img src="https://img.shields.io/badge/%20-LabFusion-0D9488?style=for-the-badge" height="44" /></a>

</div>

---

## Layout

```text
├── Aimbot.dll
├── icon.png
├── manifest.json
├── README.md
└── sources/
    ├── Aimbot.csproj
    └── *.cs
```

---

## Build

```bash
dotnet build sources/Aimbot.csproj -c Release -p:RefsRoot=/path/to/local/refs
```

---

## Changelog

**1.1.0** — BE PRIME release · NPC + Fusion player targeting · target toggles

<div align="center">

<br/>

<a href="https://t.me/be_primex"><img src="https://img.shields.io/badge/%20-Join_%40be__primex-FF0033?style=for-the-badge&logo=telegram&logoColor=white" height="48" /></a>

<br/><br/>

**Author: BE PRIME · @be_primex**

</div>
