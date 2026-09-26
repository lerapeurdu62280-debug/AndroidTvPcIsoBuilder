# Outils de test (hors solution)

## BuildIso
Génère une ISO depuis le code courant, sans l'interface :

```
dotnet run --project tools/BuildIso -- "D:\ISO\lineage-21.0-20260331-UNOFFICIAL-x86_64_tv-signed.iso" "D:\Logiciels_SOSINFOLUDO\VMs\ATV-Lineage-logo.iso"
```

Pour une ISO de diagnostic, modifier temporairement `SilentBootConfig.SilentKernelArguments`
en ajoutant `console=ttyS0,115200` **en dernier** (la dernière console devient `/dev/console`),
puis restaurer.

## vm-test
- `boot-series.ps1 -Tag fix -Count 5` : démarrages répétés de la VM `ATV-Test`, détection d'écran figé.
- `serial-relay.ps1` : relais TCP vers la console série de la VM (lecture des journaux, envoi de commandes).

Prérequis VM : Hyper-V désactivé sur l'hôte, `--cpu-profile "AMD Ryzen 7 1800X Eight-Core"`,
VMSVGA + 3D, EFI.
