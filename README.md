# AndroidTvPcIsoBuilder

Logiciel WPF (.NET 10) pour générer des images ISO Android TV personnalisées, bootables sur PC x86/x86_64, à partir d'une base Android-x86.

## Fonctionnalités

- **Génération d'ISO bootable** : reconstruit une image ISO à partir d'une source Android-x86, en préservant le catalogue de boot El Torito (BIOS et/ou UEFI) et en corrigeant la Boot Info Table ISOLINUX pour garantir un boot réel.
- **Téléchargement d'ISO source** intégré (Android-x86 9.0-r2, 32/64 bits).
- **Injection d'applications APK** par glisser-déposer, embarquées dans l'image générée.
- **Catalogue d'applications** intégré (36 apps) résolu dynamiquement via l'API officielle F-Droid.
- **Modes de boot** configurables : BIOS, UEFI ou Hybrid.
- **Vérification post-génération** de l'ISO produite (lisibilité, présence de l'image de boot, apps embarquées).

## Architecture

Le projet suit une architecture Clean/Onion :

| Projet | Rôle |
|---|---|
| `AndroidTvPcIsoBuilder.Domain` | Entités et règles métier, sans dépendance externe |
| `AndroidTvPcIsoBuilder.Application` | Cas d'usage, orchestration, interfaces |
| `AndroidTvPcIsoBuilder.Infrastructure` | Implémentations concrètes (ISO, téléchargement, persistance) |
| `AndroidTvPcIsoBuilder.Presentation.Wpf` | Interface utilisateur WPF (MVVM, CommunityToolkit.Mvvm) |
| `AndroidTvPcIsoBuilder.Tests` | Tests unitaires (MSTest) |

## Prérequis

- Windows
- .NET 10 SDK

## Compilation et lancement

```
dotnet build
dotnet run --project AndroidTvPcIsoBuilder.Presentation.Wpf
```

## Tests

```
dotnet test
```

## Notes techniques

- La reconstruction d'image ISO s'appuie sur [DiscUtils](https://github.com/DiscUtils/DiscUtils) pour la lecture/écriture ISO9660, complétée par une manipulation binaire directe du catalogue de boot El Torito (non géré nativement par DiscUtils pour les images hybrides).
- Le patch de la Boot Info Table ISOLINUX est appliqué sur l'image de boot au moment de sa relocalisation en fin de disque — c'est cette copie relocalisée, et non le fichier `isolinux.bin` resté dans l'arborescence, qui est chargée par le BIOS au démarrage.
