<div align="center">

# 📺 AndroidTvPcIsoBuilder

**Transformez n'importe quel PC en boîtier Android TV — sans manipulation manuelle d'ISO.**

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)](https://github.com/lerapeurdu62280-debug/AndroidTvPcIsoBuilder)
[![WPF](https://img.shields.io/badge/UI-WPF%20%2F%20MVVM-blueviolet?style=flat-square)](https://github.com/lerapeurdu62280-debug/AndroidTvPcIsoBuilder)
[![License](https://img.shields.io/badge/License-Non%20spécifiée-lightgrey?style=flat-square)](#)
[![Latest Release](https://img.shields.io/github/v/release/lerapeurdu62280-debug/AndroidTvPcIsoBuilder?style=flat-square&color=success)](https://github.com/lerapeurdu62280-debug/AndroidTvPcIsoBuilder/releases/latest)

</div>

---

## ✨ Ce que fait le logiciel

**AndroidTvPcIsoBuilder** génère des images ISO **Android TV bootables** pour PC x86/x86_64, prêtes à graver ou à booter en clé USB. Il gère de bout en bout : téléchargement de la base système, ajout de vos applications, et reconstruction d'une image ISO qui boote **réellement** — un point loin d'être trivial sur ce type d'image hybride (voir la section technique ci-dessous).

| | |
|---|---|
| 💿 **Génération d'ISO bootable** | Reconstruit l'image en préservant le catalogue de boot El Torito (BIOS **et/ou** UEFI) et en corrigeant la Boot Info Table ISOLINUX pour garantir un boot réel. |
| ⬇️ **Téléchargement de base système intégré** | Android‑x86 9.0 (32/64 bits), **LineageOS TV** (Android 14, launcher **Leanback** natif) *ou* **Google TV** (Android 14, interface Google TV avec recommandations — build communautaire AndroidTV-x86_64/MRDTeam). |
| 📦 **Injection d'applications** | Glissez-déposez des APK : ils sont embarqués dans l'image et installés automatiquement à la fin du premier démarrage (bases Google TV / LineageOS TV). |
| 🎬 **Logo de démarrage animé** | Génère une animation de démarrage Android à partir de votre logo et remplace celle d'origine. |
| 🧩 **Catalogue d'apps intégré** | 36 applications prêtes à ajouter, résolues dynamiquement via l'**API officielle F-Droid**. |
| ⚙️ **Modes de boot configurables** | BIOS, UEFI, ou Hybrid — au choix selon la machine cible. |
| ✅ **Vérification post-génération** | Contrôle automatique de l'ISO produite : lisibilité, présence de l'image de boot, apps effectivement embarquées. |

---

## 🚀 Installation

Téléchargez le dernier installeur depuis la page **[Releases](https://github.com/lerapeurdu62280-debug/AndroidTvPcIsoBuilder/releases/latest)** :

```
AndroidTvPcIsoBuilder-Setup.exe
```

Lancez-le, suivez l'assistant — c'est tout. Windows x64 requis.

---

## 🏗️ Architecture

Le projet suit une architecture **Clean / Onion**, avec une séparation stricte entre logique métier et détails techniques :

```
Domain            ← règles métier pures, aucune dépendance externe
   ↑
Application       ← cas d'usage, orchestration, interfaces
   ↑
Infrastructure    ← implémentations concrètes (ISO, téléchargement, persistance)
   ↑
Presentation.Wpf  ← interface utilisateur (WPF, MVVM, CommunityToolkit.Mvvm)
```

| Projet | Rôle |
|---|---|
| `AndroidTvPcIsoBuilder.Domain` | Entités et règles métier, sans dépendance externe |
| `AndroidTvPcIsoBuilder.Application` | Cas d'usage, orchestration, interfaces |
| `AndroidTvPcIsoBuilder.Infrastructure` | Implémentations concrètes (ISO, téléchargement, persistance) |
| `AndroidTvPcIsoBuilder.Presentation.Wpf` | Interface utilisateur WPF (MVVM) |
| `AndroidTvPcIsoBuilder.Tests` | Tests unitaires (MSTest) |

---

## 🛠️ Développement

### Prérequis

- Windows
- .NET 10 SDK

### Compiler et lancer

```bash
dotnet build
dotnet run --project AndroidTvPcIsoBuilder.Presentation.Wpf
```

### Lancer les tests

```bash
dotnet test
```

### Construire l'installeur

Le script [`installer/setup.iss`](installer/setup.iss) (Inno Setup) génère `AndroidTvPcIsoBuilder-Setup.exe` à partir d'une publication self-contained :

```bash
dotnet publish AndroidTvPcIsoBuilder.Presentation.Wpf -c Release -r win-x64 --self-contained true -o publish
iscc installer/setup.iss
```

---

## 🔬 Notes techniques

<details>
<summary><b>Pourquoi une ISO Android-x86 « reconstruite » ne boote pas toujours (et comment c'est résolu ici)</b></summary>

<br>

La reconstruction d'image ISO s'appuie sur [DiscUtils](https://github.com/DiscUtils/DiscUtils) pour la lecture/écriture ISO9660, complétée par une manipulation binaire directe du catalogue de boot **El Torito** (non géré nativement par DiscUtils pour les images hybrides BIOS+UEFI).

Le point délicat : ISOLINUX embarque dans son propre binaire de boot (`isolinux.bin`) un **checksum** et un **LBA** calculés au moment de l'assemblage d'origine de l'ISO. Toute reconstruction qui replace ce fichier à un autre secteur — même sans en modifier un seul octet — invalide cette *Boot Info Table*, ce qui fait échouer le boot avec `ISOLINUX: Image checksum error, sorry...`.

Pire : lors de la relocalisation du catalogue de boot en fin de disque, c'est une **copie** de l'image qui est réellement chargée par le BIOS — pas le fichier resté dans l'arborescence du système de fichiers. Le patch de la Boot Info Table est donc appliqué **au moment de cette relocalisation**, directement sur les données qui seront effectivement exécutées, avec leur LBA final réel.

Validé par des boots complets en environnement QEMU, jusqu'à l'interface Android pleinement opérationnelle.

</details>

<details>
<summary><b>Comment les applications et le logo sont branchés sur un système en lecture seule</b></summary>

<br>

Sur ces images, le système Android est un `system.img` (ext4) compressé dans `system.sfs` (squashfs) : impossible d'y écrire sans tout recompresser. Le logiciel s'appuie à la place sur un mécanisme de l'initrd Android-x86/BlissOS : au démarrage, chaque fichier du dossier `scripts/` à la racine de l'ISO est exécuté juste après le montage du système, avant le lancement d'Android.

Le script ajouté (`scripts/atvbuilder`) utilise `mount --bind`, comme l'initrd d'origine, pour :

- remplacer `system/product/media/bootanimation.zip` par l'animation générée (`bootanim/`) ;
- présenter à la place de `system/etc/user_app/` un dossier en mémoire contenant les APK d'origine et ceux de l'ISO (`apps/`), que `init.sh` installe avec `pm install` à la fin du démarrage.

Les pilotes Wi-Fi/Bluetooth ne sont pas injectés : les images supportées embarquent déjà les modules et firmwares de chacun de leurs noyaux (Intel, Realtek, Broadcom, Atheros/Qualcomm, MediaTek).

</details>

